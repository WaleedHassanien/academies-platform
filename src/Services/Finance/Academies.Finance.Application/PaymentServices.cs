using System.Security.Cryptography;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using Academies.Finance.Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Finance.Application;

// ---------- DTOs ----------

public sealed record CreatePaymentPlanRequest(long StudentUserId, decimal MonthlyAmount, DateOnly StartDate, int Months, int DueDay = 1, string? Notes = null);

public sealed record PaymentPlanDto(
    long Id, long StudentUserId, string? StudentName, decimal MonthlyAmount, string Currency, DateOnly StartDate, DateOnly EndDate, int DueDay,
    string Status, int Months, decimal TotalAmount, decimal PaidAmount);

public sealed record StudentPaymentDto(
    long Id, long PaymentPlanId, long StudentUserId, int MonthNumber, DateOnly PeriodStart, DateOnly DueDate, decimal Amount,
    decimal PaidAmount, decimal Remaining, string Currency, string Status, DateTime? PaidOnUtc);

public sealed record StudentPaymentsDto(
    long StudentUserId, string? StudentName, IReadOnlyList<StudentPaymentDto> Months, decimal TotalDue, decimal TotalPaid, decimal Outstanding, string Currency);

public sealed record RecordPaymentRequest(decimal Amount, PaymentMethod Method, string? Reference, string? Note);

public sealed record RefundRequest(decimal Amount, string Note);

public sealed record PaymentLogDto(
    long Id, long StudentPaymentId, long StudentUserId, string? StudentName, long? ParentUserId, int MonthNumber, decimal Amount,
    string Currency, string Action, string? PaidByRole, string? Method, string? Reference, string? Note, DateTime CreatedAt);

public sealed record PaymentLogQuery(long? StudentUserId = null, DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 50);

/// <summary>Month-by-month log with running totals (US-030).</summary>
public sealed record PaymentLogPageDto(PagedResult<PaymentLogDto> Logs, decimal TotalPaid, decimal TotalRefunded);

/// <summary>Where to send the payer; <see cref="ChargedAmount"/> is what the provider will charge (e.g. USD for an EGP month).</summary>
public sealed record CheckoutDto(string CheckoutUrl, string Reference, decimal Amount, string Currency, decimal ChargedAmount, string ChargedCurrency);

// ---------- Plans and monthly payments (US-029) ----------

public interface IPaymentPlanService
{
    Task<PaymentPlanDto> CreateAsync(CreatePaymentPlanRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentPlanDto>> ListAsync(long? studentUserId, CancellationToken ct = default);
    Task<PaymentPlanDto> CancelAsync(long planId, CancellationToken ct = default);
    Task<StudentPaymentsDto> StudentPaymentsAsync(long studentUserId, CancellationToken ct = default);
}

internal sealed class PaymentPlanService(
    IFinanceDbContext db, FinanceAccess access, IFinanceSettingsService settings, IReportCache reports, IAuditTrail audit, TimeProvider clock)
    : IPaymentPlanService
{
    /// <summary>Creates the plan and one payment row per month, each logged as Created.</summary>
    public async Task<PaymentPlanDto> CreateAsync(CreatePaymentPlanRequest request, CancellationToken ct = default)
    {
        var student = await db.People.FirstOrDefaultAsync(p => p.UserId == request.StudentUserId, ct);
        if (student is null || !student.HasRole(Roles.Student))
        {
            throw new BusinessRuleException($"User {request.StudentUserId} is not a student of this academy.");
        }

        var currency = await settings.CurrencyAsync(ct);
        var plan = new PaymentPlan
        {
            StudentUserId = request.StudentUserId,
            MonthlyAmount = request.MonthlyAmount,
            Currency = currency,
            StartDate = request.StartDate,
            EndDate = request.StartDate.AddMonths(request.Months).AddDays(-1),
            DueDay = request.DueDay,
            Notes = request.Notes,
        };
        db.PaymentPlans.Add(plan);
        await db.SaveChangesAsync(ct);

        var parent = await ParentOfAsync(request.StudentUserId, ct);
        var nextMonth = (await db.StudentPayments.Where(p => p.StudentUserId == request.StudentUserId).MaxAsync(p => (int?)p.MonthNumber, ct) ?? 0) + 1;
        var payments = new List<StudentPayment>();
        for (var i = 0; i < request.Months; i++)
        {
            var periodStart = request.StartDate.AddMonths(i);
            var due = new DateOnly(periodStart.Year, periodStart.Month, Math.Min(request.DueDay, DateTime.DaysInMonth(periodStart.Year, periodStart.Month)));
            payments.Add(new StudentPayment
            {
                PaymentPlanId = plan.Id,
                StudentUserId = request.StudentUserId,
                MonthNumber = nextMonth + i,
                PeriodStart = periodStart,
                DueDate = due < periodStart ? periodStart : due,
                Amount = request.MonthlyAmount,
                Currency = currency,
            });
        }

        db.StudentPayments.AddRange(payments);
        await db.SaveChangesAsync(ct);

        db.PaymentLogs.AddRange(payments.Select(p => new PaymentLog
        {
            StudentPaymentId = p.Id, StudentUserId = p.StudentUserId, ParentUserId = parent, MonthNumber = p.MonthNumber,
            Amount = p.Amount, Currency = p.Currency, Action = PaymentAction.Created, PaidByRole = access.PrimaryRole,
        }));
        await audit.RecordAsync("payments.plan_create", nameof(PaymentPlan), plan.Id, request, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);

        return (await ListAsync(request.StudentUserId, ct)).First(p => p.Id == plan.Id);
    }

    public async Task<IReadOnlyList<PaymentPlanDto>> ListAsync(long? studentUserId, CancellationToken ct = default)
    {
        var q = db.PaymentPlans.AsNoTracking().AsQueryable();
        if (studentUserId is { } sid)
        {
            await access.EnsureCanSeeStudentAsync(sid, ct);
            q = q.Where(p => p.StudentUserId == sid);
        }
        else if (await access.VisibleStudentIdsAsync(ct) is { } visible)
        {
            q = q.Where(p => visible.Contains(p.StudentUserId));
        }

        var plans = await q.OrderByDescending(p => p.Id).Take(500).ToListAsync(ct);
        var ids = plans.Select(p => p.Id).ToList();
        var totals = await db.StudentPayments.Where(p => ids.Contains(p.PaymentPlanId) && p.Status != PaymentStatus.Cancelled)
            .GroupBy(p => p.PaymentPlanId)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(x => x.Amount), Paid = g.Sum(x => x.PaidAmount) })
            .ToDictionaryAsync(x => x.Key, ct);
        var names = await db.People.NamesAsync(plans.Select(p => p.StudentUserId), ct);

        return plans.Select(p => new PaymentPlanDto(
            p.Id, p.StudentUserId, names.GetValueOrDefault(p.StudentUserId), p.MonthlyAmount, p.Currency, p.StartDate, p.EndDate, p.DueDay, p.Status.ToString(),
            totals.GetValueOrDefault(p.Id)?.Count ?? 0, totals.GetValueOrDefault(p.Id)?.Total ?? 0, totals.GetValueOrDefault(p.Id)?.Paid ?? 0)).ToList();
    }

    /// <summary>Stops the plan: unpaid months are cancelled, paid ones stay.</summary>
    public async Task<PaymentPlanDto> CancelAsync(long planId, CancellationToken ct = default)
    {
        var plan = await db.PaymentPlans.FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new NotFoundException(nameof(PaymentPlan), planId);
        plan.Status = PlanStatus.Cancelled;

        var parent = await ParentOfAsync(plan.StudentUserId, ct);
        var open = await db.StudentPayments.Where(p => p.PaymentPlanId == planId && p.PaidAmount == 0 && p.Status != PaymentStatus.Cancelled).ToListAsync(ct);
        foreach (var payment in open)
        {
            payment.Status = PaymentStatus.Cancelled;
            db.PaymentLogs.Add(new PaymentLog
            {
                StudentPaymentId = payment.Id, StudentUserId = payment.StudentUserId, ParentUserId = parent, MonthNumber = payment.MonthNumber,
                Amount = payment.Amount, Currency = payment.Currency, Action = PaymentAction.Cancelled, PaidByRole = access.PrimaryRole,
            });
        }

        await audit.RecordAsync("payments.plan_cancel", nameof(PaymentPlan), planId, null, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);
        return (await ListAsync(plan.StudentUserId, ct)).First(p => p.Id == planId);
    }

    /// <summary>The student's monthly table with Paid/Due/Overdue (US-029).</summary>
    public async Task<StudentPaymentsDto> StudentPaymentsAsync(long studentUserId, CancellationToken ct = default)
    {
        await access.EnsureCanSeeStudentAsync(studentUserId, ct);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rows = await db.StudentPayments.AsNoTracking().Where(p => p.StudentUserId == studentUserId).OrderBy(p => p.MonthNumber).ToListAsync(ct);
        var name = (await db.People.NamesAsync([studentUserId], ct)).GetValueOrDefault(studentUserId);
        var months = rows.Select(p => ToDto(p, today)).ToList();
        var live = months.Where(m => m.Status != nameof(PaymentStatus.Cancelled)).ToList();

        var currency = rows.LastOrDefault()?.Currency ?? await settings.CurrencyAsync(ct);
        return new StudentPaymentsDto(
            studentUserId, name, months, live.Sum(m => m.Amount), live.Sum(m => m.PaidAmount), live.Sum(m => m.Remaining), currency);
    }

    internal static StudentPaymentDto ToDto(StudentPayment p, DateOnly today) => new(
        p.Id, p.PaymentPlanId, p.StudentUserId, p.MonthNumber, p.PeriodStart, p.DueDate, p.Amount, p.PaidAmount, p.Remaining, p.Currency,
        p.ComputeStatus(today).ToString(), p.PaidOnUtc);

    private async Task<long?> ParentOfAsync(long studentUserId, CancellationToken ct) =>
        await db.Guardians.Where(g => g.StudentUserId == studentUserId).Select(g => (long?)g.ParentUserId).FirstOrDefaultAsync(ct);
}

// ---------- Payments, refunds and the payment log (US-030) ----------

public interface IPaymentService
{
    Task<StudentPaymentDto> RecordAsync(long studentPaymentId, RecordPaymentRequest request, CancellationToken ct = default);
    Task<StudentPaymentDto> RefundAsync(long studentPaymentId, RefundRequest request, CancellationToken ct = default);
    Task<PaymentLogPageDto> LogsAsync(PaymentLogQuery query, CancellationToken ct = default);

    /// <summary>Applies money to a monthly payment. Also used by the online-payment webhook.</summary>
    Task<StudentPayment> ApplyPaymentAsync(StudentPayment payment, decimal amount, PaymentMethod method, string? reference, string? note, string? paidByRole, CancellationToken ct);
}

internal sealed class PaymentService(
    IFinanceDbContext db, FinanceAccess access, IReportCache reports, IEventPublisher events, IAuditTrail audit, TimeProvider clock) : IPaymentService
{
    public async Task<StudentPaymentDto> RecordAsync(long studentPaymentId, RecordPaymentRequest request, CancellationToken ct = default)
    {
        var payment = await LoadAsync(studentPaymentId, ct);
        await ApplyPaymentAsync(payment, request.Amount, request.Method, request.Reference, request.Note, access.PrimaryRole, ct);
        return PaymentPlanService.ToDto(payment, Today);
    }

    public async Task<StudentPayment> ApplyPaymentAsync(
        StudentPayment payment, decimal amount, PaymentMethod method, string? reference, string? note, string? paidByRole, CancellationToken ct)
    {
        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new BusinessRuleException("This month was cancelled.");
        }

        if (amount > payment.Remaining)
        {
            throw new BusinessRuleException($"Amount exceeds the remaining {payment.Remaining:0.00}.");
        }

        payment.PaidAmount += amount;
        payment.Status = payment.ComputeStatus(Today);
        var action = payment.Status == PaymentStatus.Paid ? PaymentAction.Paid : PaymentAction.PartiallyPaid;
        if (payment.Status == PaymentStatus.Paid)
        {
            payment.PaidOnUtc = clock.GetUtcNow().UtcDateTime;
        }

        var parents = await ParentsAsync(payment.StudentUserId, ct);
        db.PaymentLogs.Add(new PaymentLog
        {
            StudentPaymentId = payment.Id, StudentUserId = payment.StudentUserId, ParentUserId = parents.Count > 0 ? parents[0] : null,
            MonthNumber = payment.MonthNumber, Amount = amount, Currency = payment.Currency, Action = action, PaidByRole = paidByRole, Method = method,
            Reference = reference, Note = note,
        });
        await events.PublishAsync(new PaymentRecorded(payment.AcademyId, payment.Id, payment.StudentUserId, parents, amount, action.ToString()), ct);
        await audit.RecordAsync("payments.record", nameof(StudentPayment), payment.Id, new { amount, method, reference }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(payment.AcademyId, ct);
        return payment;
    }

    public async Task<StudentPaymentDto> RefundAsync(long studentPaymentId, RefundRequest request, CancellationToken ct = default)
    {
        var payment = await LoadAsync(studentPaymentId, ct);
        if (request.Amount > payment.PaidAmount)
        {
            throw new BusinessRuleException($"Cannot refund more than the paid {payment.PaidAmount:0.00}.");
        }

        payment.PaidAmount -= request.Amount;
        payment.PaidOnUtc = null;
        payment.Status = payment.ComputeStatus(Today);

        var parents = await ParentsAsync(payment.StudentUserId, ct);
        db.PaymentLogs.Add(new PaymentLog
        {
            StudentPaymentId = payment.Id, StudentUserId = payment.StudentUserId, ParentUserId = parents.Count > 0 ? parents[0] : null,
            MonthNumber = payment.MonthNumber, Amount = request.Amount, Currency = payment.Currency, Action = PaymentAction.Refunded, PaidByRole = access.PrimaryRole, Note = request.Note,
        });
        await events.PublishAsync(new PaymentRecorded(payment.AcademyId, payment.Id, payment.StudentUserId, parents, request.Amount, nameof(PaymentAction.Refunded)), ct);
        await audit.RecordAsync("payments.refund", nameof(StudentPayment), payment.Id, request, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(payment.AcademyId, ct);
        return PaymentPlanService.ToDto(payment, Today);
    }

    /// <summary>
    /// US-030 visibility: admins and accountants see every log; a parent sees only their
    /// children's; a student sees only their own.
    /// </summary>
    public async Task<PaymentLogPageDto> LogsAsync(PaymentLogQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.PaymentLogs.AsNoTracking().AsQueryable();

        var visible = await access.VisibleStudentIdsAsync(ct);
        if (visible is not null)
        {
            q = q.Where(l => visible.Contains(l.StudentUserId));
        }

        if (query.StudentUserId is { } sid)
        {
            await access.EnsureCanSeeStudentAsync(sid, ct);
            q = q.Where(l => l.StudentUserId == sid);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(l => l.CreatedOnUtc >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            q = q.Where(l => l.CreatedOnUtc < toUtc);
        }

        var total = await q.CountAsync(ct);
        var paid = await q.Where(l => l.Action == PaymentAction.Paid || l.Action == PaymentAction.PartiallyPaid).SumAsync(l => (decimal?)l.Amount, ct) ?? 0;
        var refunded = await q.Where(l => l.Action == PaymentAction.Refunded).SumAsync(l => (decimal?)l.Amount, ct) ?? 0;
        var rows = await q.OrderBy(l => l.StudentUserId).ThenBy(l => l.MonthNumber).ThenBy(l => l.Id)
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.Select(r => r.StudentUserId), ct);

        var items = rows.Select(l => new PaymentLogDto(
            l.Id, l.StudentPaymentId, l.StudentUserId, names.GetValueOrDefault(l.StudentUserId), l.ParentUserId, l.MonthNumber, l.Amount,
            l.Currency, l.Action.ToString(), l.PaidByRole, l.Method?.ToString(), l.Reference, l.Note, l.CreatedOnUtc)).ToList();

        return new PaymentLogPageDto(
            new PagedResult<PaymentLogDto> { Items = items, Page = page.SafePage, PageSize = page.SafePageSize, TotalCount = total },
            paid, refunded);
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private async Task<StudentPayment> LoadAsync(long id, CancellationToken ct) =>
        await db.StudentPayments.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException(nameof(StudentPayment), id);

    private async Task<List<long>> ParentsAsync(long studentUserId, CancellationToken ct) =>
        await db.Guardians.Where(g => g.StudentUserId == studentUserId).Select(g => g.ParentUserId).ToListAsync(ct);
}

// ---------- Online payments (US-039) ----------

public interface IOnlinePaymentService
{
    Task<CheckoutDto> StartCheckoutAsync(long studentPaymentId, CancellationToken ct = default);

    /// <summary>Marks the payment succeeded (idempotent). Called from verified webhooks.</summary>
    Task CompleteAsync(string reference, bool succeeded, CancellationToken ct = default);
}

internal sealed class OnlinePaymentService(
    IFinanceDbContext db,
    FinanceAccess access,
    IEntitlementsProvider entitlements,
    IPaymentGateway gateway,
    IPaymentService payments,
    TimeProvider clock) : IOnlinePaymentService
{
    public async Task<CheckoutDto> StartCheckoutAsync(long studentPaymentId, CancellationToken ct = default)
    {
        var payment = await db.StudentPayments.FirstOrDefaultAsync(p => p.Id == studentPaymentId, ct)
            ?? throw new NotFoundException(nameof(StudentPayment), studentPaymentId);
        await access.EnsureCanSeeStudentAsync(payment.StudentUserId, ct);
        await entitlements.EnsureFeatureAsync(payment.AcademyId, FeatureKeys.PaymentGateway, ct);
        if (payment.Remaining <= 0 || payment.Status == PaymentStatus.Cancelled)
        {
            throw new BusinessRuleException("Nothing to pay for this month.");
        }

        var reference = $"PAY-{payment.AcademyId}-{payment.Id}-{RandomNumberGenerator.GetHexString(8)}";
        var email = await db.People.Where(p => p.UserId == access.Me).Select(p => p.Email).FirstOrDefaultAsync(ct);
        var session = await gateway.CreateCheckoutAsync(
            new CheckoutRequest(reference, payment.Remaining, payment.Currency, $"Tuition month {payment.MonthNumber}", email), ct);

        db.OnlinePayments.Add(new OnlinePayment
        {
            StudentPaymentId = payment.Id, Provider = gateway.Name, Reference = reference, ProviderSessionId = session.ProviderSessionId,
            Amount = payment.Remaining, Currency = payment.Currency, ChargedAmount = session.ChargedAmount,
            ChargedCurrency = session.ChargedCurrency, CheckoutUrl = session.CheckoutUrl, InitiatedByUserId = access.Me,
        });
        await db.SaveChangesAsync(ct);
        return new CheckoutDto(session.CheckoutUrl, reference, payment.Remaining, payment.Currency, session.ChargedAmount, session.ChargedCurrency);
    }

    public async Task CompleteAsync(string reference, bool succeeded, CancellationToken ct = default)
    {
        var online = await db.OnlinePayments.FirstOrDefaultAsync(o => o.Reference == reference, ct)
            ?? throw new NotFoundException(nameof(OnlinePayment), reference);
        if (online.Status != OnlinePaymentStatus.Pending)
        {
            return; // webhooks retry; apply once
        }

        online.Status = succeeded ? OnlinePaymentStatus.Succeeded : OnlinePaymentStatus.Failed;
        online.CompletedOnUtc = clock.GetUtcNow().UtcDateTime;

        if (succeeded)
        {
            var payment = await db.StudentPayments.FirstAsync(p => p.Id == online.StudentPaymentId, ct);
            var amount = Math.Min(online.Amount, payment.Remaining);
            if (amount > 0)
            {
                await payments.ApplyPaymentAsync(payment, amount, PaymentMethod.Online, reference, $"{online.Provider} checkout", "Online", ct);
                return;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

internal sealed class CreatePaymentPlanValidator : AbstractValidator<CreatePaymentPlanRequest>
{
    public CreatePaymentPlanValidator()
    {
        RuleFor(x => x.StudentUserId).GreaterThan(0);
        RuleFor(x => x.MonthlyAmount).GreaterThan(0);
        RuleFor(x => x.Months).InclusiveBetween(1, 60);
        RuleFor(x => x.DueDay).InclusiveBetween(1, 28);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

internal sealed class RecordPaymentValidator : AbstractValidator<RecordPaymentRequest>
{
    public RecordPaymentValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Method).IsInEnum();
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

internal sealed class RefundValidator : AbstractValidator<RefundRequest>
{
    public RefundValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
    }
}
