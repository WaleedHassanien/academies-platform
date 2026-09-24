namespace Academies.Contracts.Subscriptions;

/// <summary>Countable things a plan caps (US-014). A value of <see cref="Entitlements.Unlimited"/> means no cap.</summary>
public static class LimitKeys
{
    public const string Students = "students";
    public const string Teachers = "teachers";
    public const string Users = "users";

    public static readonly IReadOnlyList<string> All = [Students, Teachers, Users];
}

/// <summary>Plan features that switch functionality on or off.</summary>
public static class FeatureKeys
{
    public const string OnlineSessions = "online_sessions";
    public const string Certificates = "certificates";
    public const string Gamification = "gamification";
    public const string PaymentGateway = "payment_gateway";
    public const string Analytics = "analytics";

    public static readonly IReadOnlyList<string> All = [OnlineSessions, Certificates, Gamification, PaymentGateway, Analytics];
}

public static class SubscriptionStatuses
{
    public const string Trial = "Trial";
    public const string Active = "Active";
    public const string Expired = "Expired";
    public const string Suspended = "Suspended";
}

/// <summary>
/// What an academy may do right now: limits are academy override ?? plan limit (US-015).
/// Served by Subscription at <c>/internal/academies/{id}/entitlements</c>.
/// </summary>
public sealed record Entitlements(
    long AcademyId,
    string PlanCode,
    string PlanName,
    string Status,
    DateTime? TrialEndsOnUtc,
    IReadOnlyDictionary<string, int> Limits,
    IReadOnlyDictionary<string, bool> Features)
{
    public const int Unlimited = -1;

    public bool IsUsable => Status is SubscriptionStatuses.Trial or SubscriptionStatuses.Active;

    public bool HasFeature(string feature) => IsUsable && Features.TryGetValue(feature, out var on) && on;

    /// <summary>The cap for <paramref name="key"/>, or null when unlimited or undefined.</summary>
    public int? LimitFor(string key) =>
        Limits.TryGetValue(key, out var value) && value != Unlimited ? value : null;
}
