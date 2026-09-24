using Academies.BuildingBlocks.Domain;

namespace Academies.Engagement.Domain;

/// <summary>In-app notification for one user (US-035). Stored in both UI languages.</summary>
public sealed class Notification : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long UserId { get; set; }

    /// <summary>session_reminder, absence, payment_due, payment_received, salary_paid, ...</summary>
    public required string Type { get; set; }

    public required string TitleAr { get; set; }
    public required string TitleEn { get; set; }
    public string? BodyAr { get; set; }
    public string? BodyEn { get; set; }

    /// <summary>Client route to open, e.g. /parent.</summary>
    public string? Link { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadOnUtc { get; set; }
}

/// <summary>
/// Who did what and when (US-043). Platform actions have no academy, which is why this
/// entity is not an ITenantEntity; EngagementDbContext gives it its own filter.
/// </summary>
public sealed class AuditLog : BaseEntity
{
    public long? AcademyId { get; set; }
    public long? UserId { get; set; }
    public required string Service { get; set; }
    public required string Action { get; set; }
    public required string EntityName { get; set; }
    public string? EntityId { get; set; }
    public string? DataJson { get; set; }
    public DateTime OccurredOnUtc { get; set; }
}
