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
        });
        services.AddPlatformAudit();
        services.AddEntitlements(configuration);
        services.AddInternalServiceClient<IAcademicClient, AcademicClient>(configuration, "Academic");

        services.Configure<PaymentOptions>(configuration.GetSection(PaymentOptions.Section));
        var provider = configuration[$"{PaymentOptions.Section}:Provider"] ?? "Fake";
        if (provider.Equals("PayPal", StringComparison.OrdinalIgnoreCase))
        {
            var mode = configuration[$"{PaymentOptions.Section}:PayPal:Mode"] ?? "sandbox";
            services.AddHttpClient<IPaymentGateway, PayPalPaymentGateway>(c =>
            {
                c.BaseAddress = new PayPalOptions { Mode = mode }.BaseAddress;
                c.Timeout = TimeSpan.FromSeconds(30);
            });
        }
        else
        {
            services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
        }

        if (configuration.GetValue("Jobs:Enabled", true))
        {
            services.AddHostedService<PaymentReminderJob>();
        }

        return services;
    }
}

internal sealed class AcademicClient(HttpClient http) : IAcademicClient
{
    public Task<IReadOnlyList<TeacherSessionCount>> CompletedSessionCountsAsync(long academyId, int year, int month, CancellationToken ct = default) =>
        http.GetDataAsync<IReadOnlyList<TeacherSessionCount>>(
            $"internal/sessions/completed-counts?academyId={academyId}&year={year}&month={month}", ct);
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
