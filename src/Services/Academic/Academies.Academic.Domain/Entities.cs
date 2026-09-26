using Academies.BuildingBlocks.Domain;

namespace Academies.Academic.Domain;

// All academic data belongs to one academy (ITenantEntity). People are referenced by their
// Identity UserId. Names and emails come from the People read model (US-020..028).

// ---------- Profiles (US-020) ----------

public enum StudentStatus
{
    Active = 1,
    Suspended = 2,
    Graduated = 3,
    Withdrawn = 4,
}

public sealed class Student : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long UserId { get; set; }
    public string? Level { get; set; }
    public DateOnly EnrollmentDate { get; set; }
    public StudentStatus Status { get; set; } = StudentStatus.Active;

    /// <summary>IANA time zone the student lives in (e.g. "Asia/Riyadh"), so session times can be shown in their local time.</summary>
    public string? TimeZone { get; set; }

    /// <summary>Usual length of this student's one-to-one session; pre-fills scheduling.</summary>
    public int SessionMinutes { get; set; } = 30;

    /// <summary>The guardian's UserId (a user with the Parent role).</summary>
    public long? ParentUserId { get; set; }
}

public sealed class Teacher : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long UserId { get; set; }
    public string? Specialization { get; set; }
    public string? Bio { get; set; }
}

public sealed class Supervisor : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long UserId { get; set; }
    public string? Notes { get; set; }
}

public sealed class Parent : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long UserId { get; set; }
    public string? Occupation { get; set; }
}

/// <summary>One row per supervisor per weekday (US-021).</summary>
public sealed class WorkSchedule : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long SupervisorUserId { get; set; }
    public DayOfWeek Day { get; set; }
    public bool IsWorkingDay { get; set; }
    public TimeOnly? ShiftStart { get; set; }
    public TimeOnly? ShiftEnd { get; set; }
}

// ---------- Relationships (US-022, US-023) ----------

public sealed class SupervisorTeacher : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long SupervisorUserId { get; set; }
    public long TeacherUserId { get; set; }
}

public sealed class TeacherStudent : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long TeacherUserId { get; set; }
    public long StudentUserId { get; set; }
}

public sealed class Group : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public required string Name { get; set; }
    public long? CourseId { get; set; }
    public long? TeacherUserId { get; set; }
    public List<GroupStudent> Students { get; set; } = [];
}

public sealed class GroupStudent : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long GroupId { get; set; }
    public long StudentUserId { get; set; }
}

// ---------- Courses (US-024, US-036) ----------

public sealed class Course : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Level { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum MaterialType
{
    Link = 1,
    Document = 2,
    Video = 3,
}

public sealed class Material : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long CourseId { get; set; }
    public required string Title { get; set; }
    public required string Url { get; set; }
    public MaterialType Type { get; set; } = MaterialType.Link;
}

// ---------- Sessions (US-025..027, US-037) ----------

public enum SessionType
{
    Offline = 1,
    Online = 2,
}

/// <summary>Completed sessions are what teachers are paid for and students are billed for.</summary>
public enum SessionStatus
{
    Scheduled = 1,
    Completed = 2,
    Cancelled = 3,

    /// <summary>The student excused themself; see <see cref="SessionExcuse"/> for what happens instead.</summary>
    Excused = 4,
}

public sealed class Session : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public required string Title { get; set; }
    public long CourseId { get; set; }
    public long? GroupId { get; set; }

    /// <summary>Set for one-to-one sessions: the only student on the roster, and who is billed for it.</summary>
    public long? StudentUserId { get; set; }

    /// <summary>A make-up session points at the excused session it replaces.</summary>
    public long? MakeupOfSessionId { get; set; }

    /// <summary>
    /// For a one-to-one session the student missed without an excuse, the supervisor's call:
    /// true = it counts (student billed, teacher paid), false = it doesn't, null = not decided yet.
    /// </summary>
    public bool? AbsenceCounted { get; set; }

    public long? AbsenceDecidedByUserId { get; set; }
    public long TeacherUserId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }
    public SessionType Type { get; set; } = SessionType.Offline;
    public string? MeetingUrl { get; set; }
    public string? Location { get; set; }
    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;
    public string? Notes { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
    public DateTime? ReminderSentOnUtc { get; set; }

    public bool OverlapsWith(DateTime startUtc, DateTime endUtc) => StartsAtUtc < endUtc && startUtc < EndsAtUtc;
}

public enum AttendanceStatus
{
    Present = 1,
    Absent = 2,
    Late = 3,
}

public sealed class Attendance : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long SessionId { get; set; }
    public long StudentUserId { get; set; }
    public AttendanceStatus Status { get; set; }
    public string? Note { get; set; }
}

// ---------- Excuses ----------

public enum ExcuseStatus
{
    Pending = 1,
    Resolved = 2,
    Rejected = 3,
}

/// <summary>What happens to a session the student excused themself from.</summary>
public enum ExcuseResolution
{
    /// <summary>A make-up session is scheduled at another time; it is billed and paid instead.</summary>
    Rescheduled = 1,

    /// <summary>Moved to next month: a prepaid student gets one extra session in next month's package.</summary>
    CarriedOver = 2,

    /// <summary>Dropped: not billed, not paid, no credit.</summary>
    NotCounted = 3,

    /// <summary>A prepaid student's next monthly invoice is reduced by this session's price.</summary>
    DeductedNextMonth = 4,
}

/// <summary>A student's request to miss a one-to-one session, and how staff settled it.</summary>
public sealed class SessionExcuse : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long SessionId { get; set; }
    public long StudentUserId { get; set; }
    public long RequestedByUserId { get; set; }
    public string? Reason { get; set; }

    /// <summary>A time the student suggests for a make-up session, if any.</summary>
    public DateTime? PreferredStartsAtUtc { get; set; }

    public ExcuseStatus Status { get; set; } = ExcuseStatus.Pending;
    public ExcuseResolution? Resolution { get; set; }
    public long? MakeupSessionId { get; set; }
    public long? ResolvedByUserId { get; set; }
    public DateTime? ResolvedOnUtc { get; set; }
    public string? ResolutionNote { get; set; }
}

public sealed class SessionFeedback : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long SessionId { get; set; }
    public long StudentUserId { get; set; }
    public long TeacherUserId { get; set; }

    /// <summary>1..5</summary>
    public int Rating { get; set; }

    public string? Comment { get; set; }
}

// ---------- Assignments (US-036) ----------

public sealed class Assignment : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long CourseId { get; set; }
    public long? GroupId { get; set; }
    public long TeacherUserId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateTime DueAtUtc { get; set; }
    public decimal MaxScore { get; set; } = 100;
}

public sealed class AssignmentSubmission : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long AssignmentId { get; set; }
    public long StudentUserId { get; set; }
    public string? Content { get; set; }
    public string? AttachmentUrl { get; set; }
    public DateTime SubmittedAtUtc { get; set; }
    public decimal? Score { get; set; }
    public string? TeacherFeedback { get; set; }
    public DateTime? GradedAtUtc { get; set; }
}

// ---------- Certificates (US-041) ----------

public sealed class Certificate : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public long CourseId { get; set; }
    public required string Number { get; set; }
    public DateTime IssuedOnUtc { get; set; }
}

// ---------- Gamification (US-042) ----------

public sealed class PointEntry : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public int Points { get; set; }
    public required string Reason { get; set; }

    /// <summary>"attendance" or "assignment", plus the source id, so the same event never awards twice.</summary>
    public required string SourceType { get; set; }

    public long SourceId { get; set; }
}

public sealed class StudentBadge : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public required string BadgeCode { get; set; }
    public DateTime AwardedOnUtc { get; set; }
}

/// <summary>Point rules and badge thresholds (US-042).</summary>
public static class GamificationRules
{
    public const int Present = 10;
    public const int Late = 5;
    public const int OnTimeSubmission = 20;
    public const int HighScoreBonus = 10;

    /// <summary>Score share (0..1) that earns <see cref="HighScoreBonus"/>.</summary>
    public const decimal HighScoreThreshold = 0.9m;

    public sealed record Badge(string Code, string Name, int Threshold);

    public static readonly IReadOnlyList<Badge> Badges =
    [
        new("BRONZE", "Bronze", 100),
        new("SILVER", "Silver", 500),
        new("GOLD", "Gold", 1000),
        new("PLATINUM", "Platinum", 2500),
    ];

    public static int? PointsFor(AttendanceStatus status) => status switch
    {
        AttendanceStatus.Present => Present,
        AttendanceStatus.Late => Late,
        _ => null,
    };
}
