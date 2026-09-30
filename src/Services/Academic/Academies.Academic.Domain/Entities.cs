using Academies.BuildingBlocks.Domain;

namespace Academies.Academic.Domain;

// All academic data belongs to one academy (ITenantEntity). People are referenced by their
// Identity UserId. Names and emails come from the People read model (US-020..028).
// The academy teaches online only, and every session is one teacher with one student.

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

    /// <summary>Usual length of this student's session; pre-fills scheduling.</summary>
    public int SessionMinutes { get; set; } = 30;

    /// <summary>The guardian's UserId (a user with the Parent role). Optional: adult students have none.</summary>
    public long? ParentUserId { get; set; }

    /// <summary>
    /// Who pays and receives invoices and payment notices: the student themself or a Parent-role user
    /// (not necessarily the guardian, e.g. one relative paying for several students). Null: the
    /// guardian if there is one, otherwise the student.
    /// </summary>
    public long? PayerUserId { get; set; }

    /// <summary>The payer in force: the chosen one, else the guardian, else the student.</summary>
    public long EffectivePayer => PayerUserId ?? ParentUserId ?? UserId;

    /// <summary>Who receives session reports: the guardian, or the student when there is none (an adult).</summary>
    public long ReportRecipient => ParentUserId ?? UserId;
}

public sealed class Teacher : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long UserId { get; set; }
    public string? Specialization { get; set; }
    public string? Bio { get; set; }
}

/// <summary>A subject a teacher is qualified to teach. A teacher with none listed may teach any subject.</summary>
public sealed class TeacherCourse : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long TeacherUserId { get; set; }
    public long CourseId { get; set; }
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

// ---------- Subjects (US-024, US-036) ----------

/// <summary>What a subject is, so the session log can offer the right fields (Quran: memorisation, revision, mistakes).</summary>
public enum CourseKind
{
    Quran = 1,
    Arabic = 2,
    IslamicStudies = 3,
    Other = 4,
}

/// <summary>A subject taught one-to-one (Quran, Arabic, Islamic studies...). There is no fixed curriculum.</summary>
public sealed class Course : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Level { get; set; }
    public CourseKind Kind { get; set; } = CourseKind.Other;
    public bool IsActive { get; set; } = true;
}

public enum EnrollmentStatus
{
    Active = 1,
    Paused = 2,
    Ended = 3,
}

/// <summary>A student studying one subject, usually with one teacher. A student can take several subjects.</summary>
public sealed class Enrollment : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public long CourseId { get; set; }
    public long? TeacherUserId { get; set; }
    public EnrollmentStatus Status { get; set; } = EnrollmentStatus.Active;
    public DateOnly StartedOn { get; set; }
    public DateOnly? EndedOn { get; set; }
}

public enum LearningPlanStatus
{
    Active = 1,
    Achieved = 2,
    Closed = 3,
}

/// <summary>
/// The teacher's plan for one student in one subject. There is no fixed curriculum, so the teacher
/// writes the goal and the reference (free text, e.g. a surah range or a book) as the family asked.
/// </summary>
public sealed class LearningPlan : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public long CourseId { get; set; }
    public long TeacherUserId { get; set; }
    public required string Goal { get; set; }
    public string? Reference { get; set; }

    /// <summary>What is expected per session or week, e.g. "5 lines per session".</summary>
    public string? ExpectedAmount { get; set; }

    public DateOnly? TargetDate { get; set; }
    public LearningPlanStatus Status { get; set; } = LearningPlanStatus.Active;
    public string? Notes { get; set; }
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

/// <summary>Completed sessions are what teachers are paid for and students are billed for.</summary>
public enum SessionStatus
{
    Scheduled = 1,
    Completed = 2,
    Cancelled = 3,

    /// <summary>The student excused themself; see <see cref="SessionExcuse"/> for what happens instead.</summary>
    Excused = 4,
}

/// <summary>An online session: one teacher, one student, in the teacher's room.</summary>
public sealed class Session : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public required string Title { get; set; }
    public long CourseId { get; set; }

    /// <summary>The session's only student, and who is billed for it.</summary>
    public long StudentUserId { get; set; }

    /// <summary>A make-up session points at the excused session it replaces.</summary>
    public long? MakeupOfSessionId { get; set; }

    /// <summary>
    /// For a session the student missed without an excuse, the supervisor's call:
    /// true = it counts (student billed, teacher paid), false = it doesn't, null = not decided yet.
    /// </summary>
    public bool? AbsenceCounted { get; set; }

    public long? AbsenceDecidedByUserId { get; set; }
    public long TeacherUserId { get; set; }
    public DateTime StartsAtUtc { get; set; }
    public DateTime EndsAtUtc { get; set; }

    /// <summary>The teacher's room, or a link typed in by hand (Zoom, Meet...).</summary>
    public string? MeetingUrl { get; set; }

    public SessionStatus Status { get; set; } = SessionStatus.Scheduled;
    public string? Notes { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
    public DateTime? ReminderSentOnUtc { get; set; }

    /// <summary>When the session report went to the guardian (or adult student); it is sent once.</summary>
    public DateTime? ReportSentOnUtc { get; set; }

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

/// <summary>A student's request to miss a session, and how staff settled it.</summary>
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

/// <summary>
/// The teacher's log of a session: what was covered, a rating, homework and notes, plus optional
/// Quran fields. Every field is optional so the teacher writes what fits the lesson.
/// </summary>
public sealed class SessionFeedback : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long SessionId { get; set; }
    public long StudentUserId { get; set; }
    public long TeacherUserId { get; set; }

    /// <summary>1..5</summary>
    public int? Rating { get; set; }

    /// <summary>Notes to the student and family.</summary>
    public string? Comment { get; set; }

    /// <summary>What was covered in the session.</summary>
    public string? Accomplished { get; set; }

    public string? Homework { get; set; }

    /// <summary>Quran: new memorisation, e.g. "Al-Mulk 1-10".</summary>
    public string? Memorization { get; set; }

    /// <summary>Quran: revision, e.g. "Juz 29".</summary>
    public string? Revision { get; set; }

    /// <summary>Quran: mistakes in recitation.</summary>
    public int? Mistakes { get; set; }
}

// ---------- Assignments (US-036) ----------

public sealed class Assignment : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long CourseId { get; set; }

    /// <summary>For one student; null = every student enrolled in the subject.</summary>
    public long? StudentUserId { get; set; }

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

// ---------- Sales: leads and trial sessions ----------

public enum LeadStatus
{
    New = 1,
    Contacted = 2,
    TrialScheduled = 3,
    TrialDone = 4,
    Converted = 5,
    Lost = 6,
}

public enum TrialStatus
{
    Scheduled = 1,
    Attended = 2,
    NoShow = 3,
    Cancelled = 4,
}

/// <summary>
/// A prospective student (or family) handled by sales / customer service, up to a free trial
/// session with a teacher and conversion into a student account.
/// </summary>
public sealed class Lead : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public required string FullName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Country { get; set; }

    /// <summary>IANA time zone, used for the trial time and copied to the student on conversion.</summary>
    public string? TimeZone { get; set; }

    /// <summary>False when a guardian registers a child; <see cref="GuardianName"/> is then who we talk to.</summary>
    public bool IsAdult { get; set; } = true;

    public string? GuardianName { get; set; }

    /// <summary>The subject they are interested in.</summary>
    public long? CourseId { get; set; }

    /// <summary>Where they came from: website, WhatsApp, a referral...</summary>
    public string? Source { get; set; }

    public LeadStatus Status { get; set; } = LeadStatus.New;
    public string? LostReason { get; set; }
    public long? AssignedToUserId { get; set; }
    public DateOnly? NextFollowUpOn { get; set; }
    public string? Notes { get; set; }

    public long? TrialTeacherUserId { get; set; }
    public DateTime? TrialStartsAtUtc { get; set; }
    public DateTime? TrialEndsAtUtc { get; set; }
    public TrialStatus? TrialStatus { get; set; }

    /// <summary>The teacher's assessment after the trial (level, what to start with...).</summary>
    public string? TrialNotes { get; set; }

    public long? ConvertedStudentUserId { get; set; }
    public DateTime? ConvertedOnUtc { get; set; }

    /// <summary>True when a live trial occupies the teacher between the two times.</summary>
    public bool TrialOverlaps(DateTime startUtc, DateTime endUtc) =>
        TrialStatus == Domain.TrialStatus.Scheduled && TrialStartsAtUtc < endUtc && startUtc < TrialEndsAtUtc;
}

/// <summary>A note on a lead's timeline: a call, a message, a follow-up.</summary>
public sealed class LeadActivity : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long LeadId { get; set; }
    public required string Note { get; set; }
}
