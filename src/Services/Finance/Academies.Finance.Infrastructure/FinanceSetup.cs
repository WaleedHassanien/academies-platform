using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Infrastructure.Caching;
using Academies.BuildingBlocks.Infrastructure.Internal;
using Academies.BuildingBlocks.Infrastructure.Messaging;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.Contracts.Events;
using Academies.Finance.Application;
using Academies.Finance.Infrastructure.Payments;
using Academies.Finance.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Academies.Finance.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddFinanceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceDbContext<FinanceDbContext>(configuration, FinanceServiceInfo.Name);
        services.AddScoped<IFinanceDbContext>(sp => sp.GetRequiredService<FinanceDbContext>());
        services.AddPlatformCaching(configuration, FinanceServiceInfo.Name);
        services.AddPlatformMessaging<FinanceDbContext>(configuration, FinanceServiceInfo.Name, bus =>
        {
            bus.AddPeopleDirectory<FinanceDbContext>(FinanceServiceInfo.Name);
            bus.AddConsumer<StudentParentChangedConsumer>();
            bus.AddConsumer<StudentPayerChangedConsumer>();
        });
        services.AddPlatformAudit();
        services.AddEntitlements(configuration);
        services.AddInternalServiceClient<IAcademicClient, AcademicClient>(configuration, "Academic");

        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.Section));
        var provider = configuration[$"{PaymentOptions.Section}:Provider"] ?? "Fake";
        var autoPay = configuration[$"{PaymentOptions.Section}:AutoPay:Provider"] is { Length: > 0 } a
            ? a
            : provider.Equals("Stripe", StringComparison.OrdinalIgnoreCase) ? "Stripe" : "Fake";
        var usesStripe = provider.Equals("Stripe", StringComparison.OrdinalIgnoreCase) || autoPay.Equals("Stripe", StringComparison.OrdinalIgnoreCase);
        if (usesStripe)
        {
            services.AddHttpClient<StripeGateway>(c =>
            {
                c.BaseAddress = StripeGateway.BaseAddress;
                c.Timeout = TimeSpan.FromSeconds(30);
            });
        }

        if (provider.Equals("PayPal", StringComparison.OrdinalIgnoreCase))
        {
            var mode = configuration[$"{PaymentOptions.Section}:PayPal:Mode"] ?? "sandbox";
            services.AddHttpClient<IPaymentGateway, PayPalPaymentGateway>(c =>
            {
                c.BaseAddress = new PayPalOptions { Mode = mode }.BaseAddress;
                c.Timeout = TimeSpan.FromSeconds(30);
            });
        }
        else if (provider.Equals("Stripe", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IPaymentGateway>(sp => sp.GetRequiredService<StripeGateway>());
        }
        else
        {
            services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        }

        if (autoPay.Equals("Stripe", StringComparison.OrdinalIgnoreCase))
        {
            services.AddScoped<IAutoPayGateway>(sp => sp.GetRequiredService<StripeGateway>());
        }
        else if (autoPay.Equals("Fake", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IAutoPayGateway, FakeAutoPayGateway>();
        }
        else
        {
            services.AddSingleton<IAutoPayGateway, DisabledAutoPayGateway>();
        }

        if (configuration.GetValue("Jobs:Enabled", true))
        {
            services.AddHostedService<PaymentReminderJob>();
            services.AddHostedService<MonthCloseJob>();
            services.AddHostedService<AutoPayJob>();
        }

        return services;
    }
}

internal sealed class AcademicClient(HttpClient http) : IAcademicClient
{
    public Task<IReadOnlyList<TeacherSessionCount>> CompletedSessionCountsAsync(long academyId, int year, int month, CancellationToken ct = default) =>
        http.GetDataAsync<IReadOnlyList<TeacherSessionCount>>(
            $"internal/sessions/completed-counts?academyId={academyId}&year={year}&month={month}", ct);

    public Task<IReadOnlyList<LedgerSession>> LedgerAsync(
        long academyId, DateTime fromUtc, DateTime toUtc, long? teacherUserId = null, long? studentUserId = null, CancellationToken ct = default)
    {
        static string Utc(DateTime d) => Uri.EscapeDataString(DateTime.SpecifyKind(d, DateTimeKind.Utc).ToString("O"));
        var url = $"internal/sessions/ledger?academyId={academyId}&fromUtc={Utc(fromUtc)}&toUtc={Utc(toUtc)}"
                  + (teacherUserId is { } t ? $"&teacherUserId={t}" : "")
                  + (studentUserId is { } s ? $"&studentUserId={s}" : "");
        return http.GetDataAsync<IReadOnlyList<LedgerSession>>(url, ct);
    }
}

/// <summary>Keeps student→parent links for payment visibility (US-030).</summary>
internal sealed class StudentParentChangedConsumer(IGuardianSync guardians) : IConsumer<StudentParentChanged>
{
    public async Task Consume(ConsumeContext<StudentParentChanged> context)
    {
        var m = context.Message;
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(m.AcademyId));
        await guardians.SetAsync(m.AcademyId, m.StudentUserId, m.ParentUserId, context.CancellationToken);
    }
}

/// <summary>Keeps who pays for each student, for invoices and payment notices.</summary>
internal sealed class StudentPayerChangedConsumer(IGuardianSync guardians) : IConsumer<StudentPayerChanged>
{
    public async Task Consume(ConsumeContext<StudentPayerChanged> context)
    {
        var m = context.Message;
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(m.AcademyId));
        await guardians.SetPayerAsync(m.AcademyId, m.StudentUserId, m.PayerUserId, context.CancellationToken);
    }
}

/// <summary>Auto-pay switched off (Payments:AutoPay:Provider = None).</summary>
internal sealed class DisabledAutoPayGateway : IAutoPayGateway
{
    public string Name => "None";

    public Task<CardSetupSession> CreateCardSetupAsync(CardSetupRequest request, CancellationToken ct = default) =>
        throw new BusinessRuleException("Automatic card payments are not enabled for this platform.");

    public Task<SavedCard?> CompleteCardSetupAsync(string providerSetupId, CancellationToken ct = default) => Task.FromResult<SavedCard?>(null);

    public Task<CardChargeResult> ChargeAsync(CardChargeRequest request, CancellationToken ct = default) =>
        Task.FromResult(new CardChargeResult(false, null, "Automatic card payments are not enabled."));
}

/// <summary>Every hour: charges invoices that fell due on the payers' saved cards, academy by academy.</summary>
internal sealed class AutoPayJob(IServiceScopeFactory scopes, ILogger<AutoPayJob> logger) : RecurringJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        List<long> academies;
        using (CurrentUserOverride.Begin(SystemCurrentUser.Platform))
        {
            var db = services.GetRequiredService<IFinanceDbContext>();
            academies = await db.Mandates.Where(m => m.Status == Domain.MandateStatus.Active).Select(m => m.AcademyId).Distinct().ToListAsync(ct);
        }

        foreach (var academyId in academies)
        {
            try
            {
                await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(academyId));
                var charged = await scope.ServiceProvider.GetRequiredService<IAutoPayService>().ChargeDueAsync(ct);
                if (charged > 0)
                {
                    logger.LogInformation("Charged {Count} invoices on saved cards for academy {AcademyId}", charged, academyId);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Automatic charges failed for academy {AcademyId}", academyId);
            }
        }
    }
}

/// <summary>
/// Every 10 minutes: in the last hour of the month (Egypt time) each academy's month is closed —
/// a pending payout per teacher for their unpaid sessions and the students' invoices. The admin
/// then confirms each transfer. A close missed while the service was down runs in the first days after.
/// </summary>
internal sealed class MonthCloseJob(IServiceScopeFactory scopes, ILogger<MonthCloseJob> logger) : RecurringJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(10);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        List<long> academies;
        using (CurrentUserOverride.Begin(SystemCurrentUser.Platform))
        {
            var db = services.GetRequiredService<IFinanceDbContext>();
            academies = await db.People.Select(p => p.AcademyId).Distinct().ToListAsync(ct);
        }

        foreach (var academyId in academies)
        {
            try
            {
                // A fresh scope per academy so each gets its own tenant-filtered DbContext.
                await using var scope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
                using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(academyId));
                var result = await scope.ServiceProvider.GetRequiredService<IMonthCloseService>().CloseIfDueAsync(ct);
                if (result is not null)
                {
                    logger.LogInformation(
                        "Closed {Year}-{Month:00} for academy {AcademyId}: {Payouts} payouts, {Invoices} new invoices",
                        result.Year, result.Month, academyId, result.Payouts, result.InvoicesCreated);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Month close failed for academy {AcademyId}", academyId);
            }
        }
    }
}

/// <summary>Every 6 hours: refresh overdue status and send payment reminders (US-035).</summary>
internal sealed class PaymentReminderJob(IServiceScopeFactory scopes, ILogger<PaymentReminderJob> logger) : RecurringJob(scopes, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(6);

    protected override async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.Platform);
        await services.GetRequiredService<IPaymentReminderService>().RunAsync(ct);
    }
}
