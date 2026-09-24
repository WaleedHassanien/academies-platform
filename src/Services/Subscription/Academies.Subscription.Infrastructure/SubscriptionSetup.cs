using Academies.BuildingBlocks.Infrastructure.Caching;
using Academies.BuildingBlocks.Infrastructure.Messaging;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.Contracts.Events;
using Academies.Contracts.Subscriptions;
using Academies.Subscription.Application;
using Academies.Subscription.Domain;
using Academies.Subscription.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Academies.Subscription.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSubscriptionInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddServiceDbContext<SubscriptionDbContext>(configuration, SubscriptionServiceInfo.Name);
        services.AddScoped<ISubscriptionDbContext>(sp => sp.GetRequiredService<SubscriptionDbContext>());
        services.AddPlatformCaching(configuration, SubscriptionServiceInfo.Name);
        services.AddPlatformMessaging<SubscriptionDbContext>(configuration, SubscriptionServiceInfo.Name, bus =>
        {
            bus.AddConsumer<AcademyCreatedConsumer>();
            bus.AddConsumer<AcademyStatusChangedConsumer>();
        });
        services.AddPlatformAudit();
        return services;
    }

    /// <summary>Seeds the four standard plans if missing (US-014). Existing plans are never overwritten.</summary>
    public static async Task SeedPlansAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.Platform);
        var db = scope.ServiceProvider.GetRequiredService<SubscriptionDbContext>();

        foreach (var seed in PlanSeeds)
        {
            if (await db.Plans.AnyAsync(p => p.Code == seed.Code))
            {
                continue;
            }

            db.Plans.Add(new SubscriptionPlan
            {
                Code = seed.Code,
                Name = seed.Name,
                MonthlyPrice = seed.Price,
                TrialDays = seed.TrialDays,
                SortOrder = seed.Order,
                Limits = seed.Limits.Select(l => new PlanLimit { LimitKey = l.Key, Value = l.Value }).ToList(),
                Features = FeatureKeys.All.Select(f => new PlanFeature { FeatureKey = f, Enabled = seed.Features.Contains(f) }).ToList(),
            });
        }

        await db.SaveChangesAsync();
    }

    private sealed record PlanSeed(string Code, string Name, decimal Price, int TrialDays, int Order, Dictionary<string, int> Limits, string[] Features);

    private const int Unlimited = Entitlements.Unlimited;

    private static readonly PlanSeed[] PlanSeeds =
    [
        new("FREE", "Free", 0, 14, 1,
            new() { [LimitKeys.Students] = 20, [LimitKeys.Teachers] = 3, [LimitKeys.Users] = 30 },
            [FeatureKeys.Analytics]),
        new("START", "Start", 299, 0, 2,
            new() { [LimitKeys.Students] = 100, [LimitKeys.Teachers] = 10, [LimitKeys.Users] = 130 },
            [FeatureKeys.Analytics, FeatureKeys.OnlineSessions]),
        new("PRO", "Pro", 799, 0, 3,
            new() { [LimitKeys.Students] = 500, [LimitKeys.Teachers] = 50, [LimitKeys.Users] = 600 },
            [FeatureKeys.Analytics, FeatureKeys.OnlineSessions, FeatureKeys.Certificates, FeatureKeys.Gamification, FeatureKeys.PaymentGateway]),
        new("ENTERPRISE", "Enterprise", 1999, 0, 4,
            new() { [LimitKeys.Students] = Unlimited, [LimitKeys.Teachers] = Unlimited, [LimitKeys.Users] = Unlimited },
            [.. FeatureKeys.All]),
    ];
}

/// <summary>New academies start on the Free plan's trial.</summary>
internal sealed class AcademyCreatedConsumer(TrialProvisioner trials) : IConsumer<AcademyCreated>
{
    public async Task Consume(ConsumeContext<AcademyCreated> context)
    {
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.Platform);
        await trials.EnsureTrialAsync(context.Message.AcademyId, context.CancellationToken);
    }
}

/// <summary>A suspended academy loses its entitlements until reactivated.</summary>
internal sealed class AcademyStatusChangedConsumer(IAcademySubscriptionService subscriptions) : IConsumer<AcademyStatusChanged>
{
    public async Task Consume(ConsumeContext<AcademyStatusChanged> context)
    {
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.Platform);
        await subscriptions.SetSuspendedAsync(context.Message.AcademyId, context.Message.Status == "Suspended", context.CancellationToken);
    }
}
