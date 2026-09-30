using System.Globalization;
using System.Security.Cryptography;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Events;
using Academies.Contracts.Subscriptions;
using Academies.Finance.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Academies.Finance.Application;

// Automatic renewal: the payer saves a card once (on the provider's page, never on ours), and
// every invoice of that student is charged on it when it falls due. A refused card is retried at
// most AutoPayMandate.MaxAttempts times, a day apart, and the payer is told each time.

public sealed record AutoPayDto(
    long Id, long StudentUserId, long PayerUserId, string Provider, string Status, string? CardBrand, string? CardLast4,
    DateTime? ActivatedOnUtc, string? LastError);

public sealed record AutoPaySetupDto(string SetupUrl, string Reference);

public interface IAutoPayService
{
    /// <summary>The student's active (or pending) saved card, if any.</summary>
    Task<AutoPayDto?> GetAsync(long studentUserId, CancellationToken ct = default);

    /// <summary>Starts saving a card: returns the provider's page to send the payer to.</summary>
    Task<AutoPaySetupDto> StartSetupAsync(long studentUserId, CancellationToken ct = default);

    /// <summary>
    /// Finishes a card setup from the provider's return or webhook (system user of the academy in the
    /// reference). Idempotent. Returns null when the payer didn't finish.
    /// </summary>
    Task<AutoPayDto?> CompleteSetupAsync(string reference, string? providerSetupId, CancellationToken ct = default);

    Task CancelAsync(long studentUserId, CancellationToken ct = default);

    /// <summary>For the background job: charges the academy's due invoices on saved cards.</summary>
    Task<int> ChargeDueAsync(CancellationToken ct = default);
}

internal sealed class AutoPayService(
    IFinanceDbContext db,
    FinanceAccess access,
    IEntitlementsProvider entitlements,
    IAutoPayGateway gateway,
    IPaymentService payments,
    IEventPublisher events,
    IAuditTrail audit,
    TimeProvider clock,
    ILogger<AutoPayService> logger) : IAutoPayService
{
    public async Task<AutoPayDto?> GetAsync(long studentUserId, CancellationToken ct = default)
    {
        await access.EnsureCanSeeStudentAsync(studentUserId, ct);
        var mandate = await db.Mandates.AsNoTracking()
            .Where(m => m.StudentUserId == studentUserId && m.Status != MandateStatus.Cancelled)
            .OrderByDescending(m => m.Status == MandateStatus.Active).ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync(ct);
        return mandate is null ? null : ToDto(mandate);
    }

    public async Task<AutoPaySetupDto> StartSetupAsync(long studentUserId, CancellationToken ct = default)
    {
        var payer = await access.PayerOfAsync(studentUserId, ct);
        if (payer != access.Me && !access.SeesAllPayments)
        {
            throw new ForbiddenAccessException("Only the person who pays for this student can save a card.");
        }

        await entitlements.EnsureFeatureAsync(access.AcademyId, FeatureKeys.PaymentGateway, ct);
        var reference = $"CARD-{access.AcademyId}-{studentUserId}-{RandomNumberGenerator.GetHexString(8)}";
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == payer, ct);
        var student = (await db.People.NamesAsync([studentUserId], ct)).GetValueOrDefault(studentUserId, $"#{studentUserId}");
        var session = await gateway.CreateCardSetupAsync(
            new CardSetupRequest(reference, person?.Email, person?.FullName, $"Automatic tuition payments for {student}"), ct);

        db.Mandates.Add(new AutoPayMandate
        {
            StudentUserId = studentUserId, PayerUserId = payer, Provider = gateway.Name, Reference = reference, ProviderSetupId = session.ProviderSetupId,
        });
        await db.SaveChangesAsync(ct);
        return new AutoPaySetupDto(session.SetupUrl, reference);
    }

    public async Task<AutoPayDto?> CompleteSetupAsync(string reference, string? providerSetupId, CancellationToken ct = default)
    {
        var mandate = await db.Mandates.FirstOrDefaultAsync(m => m.Reference == reference, ct)
                      ?? throw new NotFoundException(nameof(AutoPayMandate), reference);
        if (mandate.Status != MandateStatus.Pending)
        {
            return ToDto(mandate);
        }

        var card = await gateway.CompleteCardSetupAsync(providerSetupId ?? mandate.ProviderSetupId ?? reference, ct);
        if (card is null)
        {
            return null;
        }

        // One active card per student: the new one replaces the old.
        var previous = await db.Mandates.Where(m => m.StudentUserId == mandate.StudentUserId && m.Status == MandateStatus.Active).ToListAsync(ct);
        previous.ForEach(m => m.Status = MandateStatus.Cancelled);

        mandate.CustomerId = card.CustomerId;
        mandate.PaymentMethodId = card.PaymentMethodId;
        mandate.CardBrand = card.Brand;
        mandate.CardLast4 = card.Last4;
        mandate.Status = MandateStatus.Active;
        mandate.ActivatedOnUtc = clock.GetUtcNow().UtcDateTime;
        mandate.LastError = null;
        await audit.RecordAsync("autopay.activate", nameof(AutoPayMandate), mandate.Id, new { mandate.StudentUserId, mandate.CardBrand, mandate.CardLast4 }, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(mandate);
    }

    public async Task CancelAsync(long studentUserId, CancellationToken ct = default)
    {
        var payer = await access.PayerOfAsync(studentUserId, ct);
        if (payer != access.Me && !access.SeesAllPayments)
        {
            throw new ForbiddenAccessException("Only the person who pays for this student can remove the card.");
        }

        var mandates = await db.Mandates.Where(m => m.StudentUserId == studentUserId && m.Status != MandateStatus.Cancelled).ToListAsync(ct);
        mandates.ForEach(m => m.Status = MandateStatus.Cancelled);
        await audit.RecordAsync("autopay.cancel", nameof(AutoPayMandate), studentUserId, null, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> ChargeDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(AcademyCalendar.ToLocal(now));
        var retryAfter = now.AddHours(-23);
        var mandates = await db.Mandates.Where(m => m.Status == MandateStatus.Active && m.PaymentMethodId != null).ToListAsync(ct);
        var charged = 0;

        foreach (var mandate in mandates)
        {
            var due = await db.StudentPayments
                .Where(p => p.StudentUserId == mandate.StudentUserId && p.Status != PaymentStatus.Cancelled && p.Status != PaymentStatus.Paid)
                .Where(p => p.DueDate <= today && p.PaidAmount < p.Amount && p.AutoChargeAttempts < AutoPayMandate.MaxAttempts)
                .Where(p => p.LastAutoChargeOnUtc == null || p.LastAutoChargeOnUtc < retryAfter)
                .OrderBy(p => p.DueDate)
                .ToListAsync(ct);

            foreach (var invoice in due)
            {
                if (await ChargeAsync(mandate, invoice, now, ct))
                {
                    charged++;
                }
            }
        }

        return charged;
    }

    private async Task<bool> ChargeAsync(AutoPayMandate mandate, StudentPayment invoice, DateTime now, CancellationToken ct)
    {
        var amount = invoice.Remaining;
        var reference = $"AUTO-{invoice.AcademyId}-{invoice.Id}-{RandomNumberGenerator.GetHexString(8)}";
        invoice.AutoChargeAttempts++;
        invoice.LastAutoChargeOnUtc = now;

        var online = new OnlinePayment
        {
            StudentPaymentId = invoice.Id, Provider = gateway.Name, Reference = reference, MandateId = mandate.Id, Amount = amount,
            Currency = invoice.Currency, ChargedAmount = amount, ChargedCurrency = invoice.Currency,
        };
        db.OnlinePayments.Add(online);
        await db.SaveChangesAsync(ct);

        CardChargeResult result;
        try
        {
            result = await gateway.ChargeAsync(new CardChargeRequest(
                reference, mandate.CustomerId!, mandate.PaymentMethodId!, amount, invoice.Currency,
                $"Tuition month {invoice.MonthNumber} ({amount.ToString("0.00", CultureInfo.InvariantCulture)} {invoice.Currency})"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Automatic charge {Reference} failed", reference);
            result = new CardChargeResult(false, null, "The payment provider could not be reached.");
        }

        online.ProviderSessionId = result.ProviderPaymentId;
        online.CompletedOnUtc = clock.GetUtcNow().UtcDateTime;
        if (result.Succeeded)
        {
            online.Status = OnlinePaymentStatus.Succeeded;
            mandate.LastError = null;
            await payments.ApplyPaymentAsync(invoice, amount, PaymentMethod.Card, reference, $"Automatic charge ({mandate.CardBrand} •••• {mandate.CardLast4})", "AutoPay", ct);
            return true;
        }

        online.Status = OnlinePaymentStatus.Failed;
        online.FailureReason = Trim(result.FailureReason);
        mandate.LastError = online.FailureReason;
        await events.PublishAsync(new AutoPayFailed(
            invoice.AcademyId, invoice.Id, invoice.StudentUserId, [mandate.PayerUserId], amount, invoice.Currency, online.FailureReason), ct);
        await db.SaveChangesAsync(ct);
        return false;
    }

    private static string? Trim(string? text) => text is { Length: > 500 } ? text[..500] : text;

    private static AutoPayDto ToDto(AutoPayMandate m) => new(
        m.Id, m.StudentUserId, m.PayerUserId, m.Provider, m.Status.ToString(), m.CardBrand, m.CardLast4, m.ActivatedOnUtc, m.LastError);
}
