using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Subscriptions;

namespace Academies.BuildingBlocks.Application.Abstractions;

/// <summary>Reads an academy's plan limits and features from the Subscription service.</summary>
public interface IEntitlementsProvider
{
    Task<Entitlements> GetAsync(long academyId, CancellationToken ct = default);
}

public static class EntitlementsExtensions
{
    /// <summary>US-017: refuse with an upgrade hint when a feature isn't in the academy's plan.</summary>
    public static async Task EnsureFeatureAsync(this IEntitlementsProvider provider, long academyId, string feature, CancellationToken ct = default)
    {
        var entitlements = await provider.GetAsync(academyId, ct);
        if (!entitlements.HasFeature(feature))
        {
            throw new BusinessRuleException(
                $"Your '{entitlements.PlanName}' plan does not include '{feature}'. Upgrade the plan to use it.",
                "plan.feature_missing");
        }
    }

    /// <summary>US-017: refuse adding <paramref name="adding"/> more when it would exceed the cap.</summary>
    public static async Task EnsureWithinLimitAsync(
        this IEntitlementsProvider provider, long academyId, string limitKey, int currentCount, int adding = 1, CancellationToken ct = default)
    {
        var entitlements = await provider.GetAsync(academyId, ct);
        if (!entitlements.IsUsable)
        {
            throw new BusinessRuleException($"The academy subscription is {entitlements.Status}. Renew it to continue.", "plan.inactive");
        }

        if (entitlements.LimitFor(limitKey) is { } cap && currentCount + adding > cap)
        {
            throw new BusinessRuleException(
                $"Plan limit reached for {limitKey}: {currentCount}/{cap}. Upgrade the plan to add more.",
                "plan.limit_reached");
        }
    }
}

/// <summary>Appends to the audit log (US-043). Call before SaveChanges: it is sent through the outbox.</summary>
public interface IAuditTrail
{
    Task RecordAsync(string action, string entityName, object? entityId, object? data = null, CancellationToken ct = default);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

/// <summary>
/// Publishes an integration event through the transactional outbox: it leaves only when the
/// next SaveChanges on the service's DbContext commits.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<T>(T message, CancellationToken ct = default) where T : class;
}
