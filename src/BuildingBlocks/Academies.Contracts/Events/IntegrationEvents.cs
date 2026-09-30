namespace Academies.Contracts.Events;

// Integration events published over RabbitMQ (MassTransit). These records are the wire contract:
// only add optional members; never rename or remove, or older consumers break.

// ---- Identity ----
public sealed record AcademyCreated(long AcademyId, string Name);

public sealed record AcademyStatusChanged(long AcademyId, string Status);

public sealed record UserCreated(long UserId, long? AcademyId, string Email, string FullName, IReadOnlyList<string> Roles);

/// <summary>Name, roles or active flag changed. Consumers upsert their people read model.</summary>
public sealed record UserUpdated(long UserId, long? AcademyId, string Email, string FullName, IReadOnlyList<string> Roles, bool IsActive);

// ---- Subscription ----
public sealed record AcademySubscriptionChanged(long AcademyId, long PlanId, string PlanCode);

// ---- Academic ----
public sealed record StudentParentChanged(long AcademyId, long StudentUserId, long? ParentUserId);

public sealed record SessionCompleted(long AcademyId, long SessionId, long TeacherUserId, DateTime StartsAtUtc);

public sealed record SessionReminderDue(long AcademyId, long SessionId, string Title, DateTime StartsAtUtc, IReadOnlyList<long> UserIds);

public sealed record AttendanceRecorded(
    long AcademyId, long SessionId, long StudentUserId, string Status, long? ParentUserId, DateTime SessionStartsAtUtc);

/// <summary>Who pays for a student (null: back to the default, the guardian or else the student).</summary>
public sealed record StudentPayerChanged(long AcademyId, long StudentUserId, long? PayerUserId);

/// <summary>
/// A held session's log, sent once to the guardian (or the adult student when there is none).
/// Every log field is optional.
/// </summary>
public sealed record SessionReportReady(
    long AcademyId,
    long SessionId,
    long StudentUserId,
    string StudentName,
    IReadOnlyList<long> RecipientUserIds,
    string CourseName,
    string TeacherName,
    DateTime StartsAtUtc,
    string? Attendance,
    int? Rating,
    string? Accomplished,
    string? Homework,
    string? Memorization,
    string? Revision,
    int? Mistakes,
    string? Comment);

// ---- Finance ----

/// <summary><see cref="RecipientUserIds"/>, when set, is who to notify (the payer) instead of the student and guardians.</summary>
public sealed record PaymentDue(
    long AcademyId,
    long StudentPaymentId,
    long StudentUserId,
    IReadOnlyList<long> ParentUserIds,
    int MonthNumber,
    decimal Amount,
    DateOnly DueDate,
    bool IsOverdue,
    IReadOnlyList<long>? RecipientUserIds = null,
    string? Currency = null);

public sealed record PaymentRecorded(
    long AcademyId, long StudentPaymentId, long StudentUserId, IReadOnlyList<long> ParentUserIds, decimal Amount, string Action,
    IReadOnlyList<long>? RecipientUserIds = null, string? Currency = null);

/// <summary>A saved card was refused when an invoice was charged automatically.</summary>
public sealed record AutoPayFailed(
    long AcademyId, long StudentPaymentId, long StudentUserId, IReadOnlyList<long> RecipientUserIds, decimal Amount, string Currency, string? Reason);

public sealed record SalaryPaid(long AcademyId, long SalaryId, long UserId, int Year, int Month, decimal Amount);

// ---- Cross-cutting ----

/// <summary>Sensitive operation to append to the audit log (US-043).</summary>
public sealed record AuditEvent(
    long? AcademyId,
    long? UserId,
    string Service,
    string Action,
    string EntityName,
    string? EntityId,
    string? DataJson,
    DateTime OccurredOnUtc);
