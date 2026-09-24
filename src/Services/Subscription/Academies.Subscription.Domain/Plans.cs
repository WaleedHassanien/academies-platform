using Academies.BuildingBlocks.Domain;

namespace Academies.Subscription.Domain;

/// <summary>A sellable package: Free (14-day trial), Start, Pro, Enterprise (US-014).</summary>
public sealed class SubscriptionPlan : BaseEntity
{
    public required string Code { get; set; }
    public required string Name { get; set; }
    public decimal MonthlyPrice { get; set; }
    public int TrialDays { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public List<PlanLimit> Limits { get; set; } = [];
    public List<PlanFeature> Features { get; set; } = [];
}

/// <summary>Cap for one <c>LimitKeys</c> value; -1 means unlimited.</summary>
public sealed class PlanLimit : BaseEntity
{
    public long PlanId { get; set; }
    public required string LimitKey { get; set; }
    public int Value { get; set; }
}

public sealed class PlanFeature : BaseEntity
{
    public long PlanId { get; set; }
    public required string FeatureKey { get; set; }
    public bool Enabled { get; set; }
}

/// <summary>Every price change, for billing history (US-016).</summary>
public sealed class PlanPriceHistory : BaseEntity
{
    public long PlanId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
}

public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,
    Expired = 3,
    Suspended = 4,
}

/// <summary>The plan an academy is on (US-015). One live row per academy.</summary>
public sealed class AcademySubscription : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long PlanId { get; set; }
    public SubscriptionPlan Plan { get; set; } = null!;
    public SubscriptionStatus Status { get; set; }
    public DateTime StartsOnUtc { get; set; }
    public DateTime? TrialEndsOnUtc { get; set; }
    public DateTime? EndsOnUtc { get; set; }

    /// <summary>Status after applying expiry dates at <paramref name="nowUtc"/>.</summary>
    public SubscriptionStatus EffectiveStatus(DateTime nowUtc) => Status switch
    {
        SubscriptionStatus.Trial when TrialEndsOnUtc < nowUtc => SubscriptionStatus.Expired,
        SubscriptionStatus.Active when EndsOnUtc < nowUtc => SubscriptionStatus.Expired,
        _ => Status,
    };
}

/// <summary>Academy-specific cap that wins over the plan's (US-015).</summary>
public sealed class AcademyLimitOverride : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public required string LimitKey { get; set; }
    public int Value { get; set; }
    public string? Reason { get; set; }
}
