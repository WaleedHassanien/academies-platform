using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

// What happened to each one-to-one session, and therefore what is billed and paid:
//  - held (completed, student present or late)      → the student is billed, the teacher is paid
//  - absent without excuse                          → the supervisor decides whether it counts
//  - excused                                        → never billed or paid; the excuse's resolution
//                                                     says what the student gets instead
// Finance reads this through the ledger and applies prices; Academic never deals with money.

public sealed record RequestExcuseRequest(string? Reason, DateTime? PreferredStartsAtUtc);

/// <summary>Resolve with a <see cref="Resolution"/>, or <see cref="Reject"/> to put the session back on.</summary>
public sealed record ResolveExcuseRequest(ExcuseResolution? Resolution, bool Reject, DateTime? MakeupStartsAtUtc, string? Note);

public sealed record AbsenceDecisionRequest(bool Counted);

public sealed record ExcuseItemDto(SessionExcuseDto Excuse, SessionDto Session, string? RequestedByName);

public static class SessionOutcomes
{
    public const string Scheduled = "Scheduled";
    public const string Held = "Held";
    public const string AbsentCounted = "AbsentCounted";
    public const string AbsentNotCounted = "AbsentNotCounted";
    public const string AbsentPending = "AbsentPending";
    public const string Excused = "Excused";
    public const string Cancelled = "Cancelled";

    /// <summary>Outcomes the student is billed for and the teacher is paid for.</summary>
    public static bool Counts(string outcome) => outcome is Held or AbsentCounted;
}

/// <summary>One one-to-one session as Finance sees it.</summary>
public sealed record LedgerSessionDto(
    long SessionId, long TeacherUserId, long StudentUserId, DateTime StartsAtUtc, int DurationMinutes, string Outcome, bool Counts,
    string? ExcuseResolution, long? MakeupOfSessionId);

public sealed record SessionCountsDto(
    int Scheduled, int Held, int AbsentCounted, int AbsentNotCounted, int AbsentPending, int Excused, int Cancelled, int HeldMinutes)
{
    public int Counted => Held + AbsentCounted;
}

public sealed record SessionReportRowDto(long UserId, string FullName, SessionCountsDto Counts);

/// <summary>The session report on each role's dashboard: totals, plus a row per teacher and per student in view.</summary>
public sealed record SessionReportDto(
    DateTime FromUtc, DateTime ToUtc, SessionCountsDto Totals, IReadOnlyList<SessionReportRowDto> ByTeacher,
    IReadOnlyList<SessionReportRowDto> ByStudent, int PendingExcuses, int PendingAbsences);

public interface ISessionOutcomeService
{
    Task<SessionDto> RequestExcuseAsync(long sessionId, RequestExcuseRequest request, CancellationToken ct = default);
    Task<SessionDto> ResolveExcuseAsync(long excuseId, ResolveExcuseRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ExcuseItemDto>> ExcusesAsync(string? status, CancellationToken ct = default);
    Task<SessionDto> DecideAbsenceAsync(long sessionId, AbsenceDecisionRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> PendingAbsencesAsync(CancellationToken ct = default);
    Task<SessionReportDto> ReportAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Service-to-service (Finance): no caller checks, the academy comes from the system user.</summary>
    Task<IReadOnlyList<LedgerSessionDto>> LedgerAsync(DateTime fromUtc, DateTime toUtc, long? teacherUserId, long? studentUserId, CancellationToken ct = default);
}

internal sealed partial class SessionService : ISessionOutcomeService
{
    // ---------- Excuses ----------

    /// <summary>
    /// The student (or their parent, teacher, supervisor or staff) excuses them from an upcoming
    /// one-to-one session. It is taken off the timetable at once; staff then settle what happens.
    /// </summary>
    public async Task<SessionDto> RequestExcuseAsync(long sessionId, RequestExcuseRequest request, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, ct);
        var studentId = session.StudentUserId ?? throw new BusinessRuleException("Only one-to-one sessions can be excused.");
        await EnsureCanActForStudentAsync(session, ct);

        if (session.Status != SessionStatus.Scheduled)
        {
            throw new BusinessRuleException("Only a scheduled session can be excused.");
        }

        var canDecide = await CanDecideAsync(session, ct);
        if (!canDecide && session.StartsAtUtc <= clock.GetUtcNow().UtcDateTime)
        {
            throw new BusinessRuleException("The session has already started; ask your supervisor instead.");
        }

        db.Excuses.Add(new SessionExcuse
        {
            SessionId = session.Id, StudentUserId = studentId, RequestedByUserId = guard.Me, Reason = request.Reason?.Trim(),
            PreferredStartsAtUtc = request.PreferredStartsAtUtc,
        });
        session.Status = SessionStatus.Excused;
        await db.SaveChangesAsync(ct);
        return await GetAsync(session.Id, ct);
    }

    public async Task<SessionDto> ResolveExcuseAsync(long excuseId, ResolveExcuseRequest request, CancellationToken ct = default)
    {
        var excuse = await db.Excuses.FirstOrDefaultAsync(e => e.Id == excuseId, ct) ?? throw new NotFoundException(nameof(SessionExcuse), excuseId);
        var session = await LoadAsync(excuse.SessionId, ct);
        await EnsureCanDecideAsync(session, ct);
        if (excuse.Status != ExcuseStatus.Pending)
        {
            throw new BusinessRuleException("This excuse has already been settled.");
        }

        excuse.ResolvedByUserId = guard.Me;
        excuse.ResolvedOnUtc = clock.GetUtcNow().UtcDateTime;
        excuse.ResolutionNote = request.Note?.Trim();

        if (request.Reject)
        {
            // Back on the timetable: the teacher runs it (or records an absence) as usual.
            excuse.Status = ExcuseStatus.Rejected;
            session.Status = SessionStatus.Scheduled;
            await EnsureNoConflictAsync(session, ct);
        }
        else
        {
            var resolution = request.Resolution ?? throw new BusinessRuleException("Choose what happens to the session.");
            excuse.Status = ExcuseStatus.Resolved;
            excuse.Resolution = resolution;
            if (resolution == ExcuseResolution.Rescheduled)
            {
                var start = request.MakeupStartsAtUtc ?? throw new BusinessRuleException("Choose a time for the make-up session.");
                var makeup = new Session
                {
                    Title = session.Title, CourseId = session.CourseId, StudentUserId = session.StudentUserId, TeacherUserId = session.TeacherUserId,
                    StartsAtUtc = start, EndsAtUtc = start + (session.EndsAtUtc - session.StartsAtUtc), Type = session.Type,
                    Location = session.Location, MakeupOfSessionId = session.Id, Notes = session.Notes,
                    MeetingUrl = session.MeetingUrl,
                };
                await EnsureNoConflictAsync(makeup, ct);
                db.Sessions.Add(makeup);
                await db.SaveChangesAsync(ct);
                excuse.MakeupSessionId = makeup.Id;
            }
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(session.Id, ct);
    }

    /// <summary>Excuses the caller can act on: all for staff, their teachers' for a supervisor, their own otherwise.</summary>
    public async Task<IReadOnlyList<ExcuseItemDto>> ExcusesAsync(string? status, CancellationToken ct = default)
    {
        var q = db.Excuses.AsNoTracking().AsQueryable();
        if (Enum.TryParse<ExcuseStatus>(status, true, out var s))
        {
            q = q.Where(e => e.Status == s);
        }

        var scoped = await ScopeAsync(db.Sessions.AsNoTracking(), ct);
        q = q.Where(e => scoped.Any(x => x.Id == e.SessionId));
        var excuses = await q.OrderByDescending(e => e.Id).Take(300).ToListAsync(ct);

        var sessionIds = excuses.Select(e => e.SessionId).Distinct().ToList();
        var sessions = (await ToDtosAsync(await db.Sessions.AsNoTracking().Where(x => sessionIds.Contains(x.Id)).ToListAsync(ct), ct))
            .ToDictionary(x => x.Id);
        var names = await db.People.NamesAsync(excuses.Select(e => e.RequestedByUserId), ct);

        return excuses.Where(e => sessions.ContainsKey(e.SessionId))
            .Select(e => new ExcuseItemDto(ToExcuseDto(e), sessions[e.SessionId], names.GetValueOrDefault(e.RequestedByUserId)))
            .ToList();
    }

    // ---------- Unexcused absences ----------

    public async Task<SessionDto> DecideAbsenceAsync(long sessionId, AbsenceDecisionRequest request, CancellationToken ct = default)
    {
        var session = await LoadAsync(sessionId, ct);
        var studentId = session.StudentUserId ?? throw new BusinessRuleException("Only one-to-one sessions have an absence decision.");
        await EnsureCanDecideAsync(session, ct);

        var absent = await db.Attendances.AnyAsync(a => a.SessionId == sessionId && a.StudentUserId == studentId && a.Status == AttendanceStatus.Absent, ct);
        if (!absent)
        {
            throw new BusinessRuleException("The student isn't marked absent for this session.");
        }

        session.AbsenceCounted = request.Counted;
        session.AbsenceDecidedByUserId = guard.Me;
        await db.SaveChangesAsync(ct);
        return await GetAsync(sessionId, ct);
    }

    /// <summary>Absences still waiting for a decision, limited to sessions the caller may decide.</summary>
    public async Task<IReadOnlyList<SessionDto>> PendingAbsencesAsync(CancellationToken ct = default)
    {
        var q = db.Sessions.AsNoTracking()
            .Where(s => s.StudentUserId != null && s.AbsenceCounted == null && s.Status != SessionStatus.Cancelled && s.Status != SessionStatus.Excused)
            .Where(s => db.Attendances.Any(a => a.SessionId == s.Id && a.StudentUserId == s.StudentUserId && a.Status == AttendanceStatus.Absent));

        if (!CanDecideAll)
        {
            var teachers = guard.IsInRole(Roles.Supervisor) ? await guard.SupervisorTeacherIdsAsync(guard.Me, ct) : [];
            q = q.Where(s => teachers.Contains(s.TeacherUserId));
        }

        return await ToDtosAsync(await q.OrderBy(s => s.StartsAtUtc).Take(300).ToListAsync(ct), ct);
    }

    // ---------- Ledger and reports ----------

    public async Task<IReadOnlyList<LedgerSessionDto>> LedgerAsync(
        DateTime fromUtc, DateTime toUtc, long? teacherUserId, long? studentUserId, CancellationToken ct = default)
    {
        var q = db.Sessions.AsNoTracking().Where(s => s.StudentUserId != null && s.StartsAtUtc >= fromUtc && s.StartsAtUtc < toUtc);
        if (teacherUserId is { } t)
        {
            q = q.Where(s => s.TeacherUserId == t);
        }

        if (studentUserId is { } st)
        {
            q = q.Where(s => s.StudentUserId == st);
        }

        var sessions = await q.OrderBy(s => s.StartsAtUtc).ToListAsync(ct);
        var outcomes = await OutcomesAsync(sessions, ct);
        return sessions.Select(s =>
        {
            var (outcome, resolution) = outcomes[s.Id];
            return new LedgerSessionDto(
                s.Id, s.TeacherUserId, s.StudentUserId!.Value, s.StartsAtUtc, (int)Math.Round((s.EndsAtUtc - s.StartsAtUtc).TotalMinutes),
                outcome, SessionOutcomes.Counts(outcome), resolution, s.MakeupOfSessionId);
        }).ToList();
    }

    /// <summary>
    /// What the caller's dashboard summarises: staff see the academy, a supervisor their
    /// teachers, a teacher their own teaching, a student (or parent) their own sessions.
    /// </summary>
    public async Task<SessionReportDto> ReportAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(370))
        {
            throw new BusinessRuleException("Choose a period of up to a year.");
        }

        var q = db.Sessions.AsNoTracking().Where(s => s.StartsAtUtc >= fromUtc && s.StartsAtUtc < toUtc);
        if (!CanDecideAll)
        {
            var teachers = new HashSet<long>();
            var students = new HashSet<long>();
            if (guard.IsInRole(Roles.Teacher))
            {
                teachers.Add(guard.Me);
            }

            if (guard.IsInRole(Roles.Supervisor))
            {
                teachers.UnionWith(await guard.SupervisorTeacherIdsAsync(guard.Me, ct));
            }

            if (guard.IsInRole(Roles.Student))
            {
                students.Add(guard.Me);
            }

            if (guard.IsInRole(Roles.Parent))
            {
                students.UnionWith(await guard.ChildrenIdsAsync(guard.Me, ct));
            }

            q = q.Where(s => teachers.Contains(s.TeacherUserId) || (s.StudentUserId != null && students.Contains(s.StudentUserId.Value)));
        }

        var sessions = await q.ToListAsync(ct);
        var outcomes = await OutcomesAsync(sessions, ct);
        var rows = sessions.Select(s => (Session: s, Outcome: outcomes[s.Id].Outcome)).ToList();

        var names = await db.People.NamesAsync(
            rows.Select(r => r.Session.TeacherUserId).Concat(rows.Where(r => r.Session.StudentUserId.HasValue).Select(r => r.Session.StudentUserId!.Value)), ct);
        List<SessionReportRowDto> Group(Func<Session, long?> key) => rows
            .Where(r => key(r.Session).HasValue)
            .GroupBy(r => key(r.Session)!.Value)
            .Select(g => new SessionReportRowDto(g.Key, names.GetValueOrDefault(g.Key, $"#{g.Key}"), Count(g)))
            .OrderBy(r => r.FullName)
            .ToList();

        var pendingExcuses = (await ExcusesAsync(nameof(ExcuseStatus.Pending), ct)).Count;
        var pendingAbsences = CanDecideAll || guard.IsInRole(Roles.Supervisor) ? (await PendingAbsencesAsync(ct)).Count : 0;

        return new SessionReportDto(
            fromUtc, toUtc, Count(rows), Group(s => s.TeacherUserId), Group(s => s.StudentUserId), pendingExcuses, pendingAbsences);
    }

    private static SessionCountsDto Count(IEnumerable<(Session Session, string Outcome)> rows)
    {
        var list = rows.ToList();
        int Of(string outcome) => list.Count(r => r.Outcome == outcome);
        return new SessionCountsDto(
            Of(SessionOutcomes.Scheduled), Of(SessionOutcomes.Held), Of(SessionOutcomes.AbsentCounted), Of(SessionOutcomes.AbsentNotCounted),
            Of(SessionOutcomes.AbsentPending), Of(SessionOutcomes.Excused), Of(SessionOutcomes.Cancelled),
            (int)list.Where(r => r.Outcome == SessionOutcomes.Held).Sum(r => (r.Session.EndsAtUtc - r.Session.StartsAtUtc).TotalMinutes));
    }

    /// <summary>The outcome of each session, and for excused ones how the excuse was settled.</summary>
    private async Task<Dictionary<long, (string Outcome, string? Resolution)>> OutcomesAsync(IReadOnlyList<Session> sessions, CancellationToken ct)
    {
        var ids = sessions.Select(s => s.Id).ToList();
        var absent = ids.Count == 0 ? [] : (await db.Attendances.AsNoTracking()
                .Where(a => ids.Contains(a.SessionId) && a.Status == AttendanceStatus.Absent)
                .Select(a => new { a.SessionId, a.StudentUserId })
                .ToListAsync(ct))
            .Select(a => (a.SessionId, a.StudentUserId))
            .ToHashSet();
        var excuses = ids.Count == 0 ? [] : (await db.Excuses.AsNoTracking().Where(e => ids.Contains(e.SessionId)).ToListAsync(ct))
            .GroupBy(e => e.SessionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Id).First());

        return sessions.ToDictionary(s => s.Id, s =>
        {
            if (s.Status == SessionStatus.Excused)
            {
                return (SessionOutcomes.Excused, excuses.GetValueOrDefault(s.Id)?.Resolution?.ToString());
            }

            if (s.Status == SessionStatus.Cancelled)
            {
                return (SessionOutcomes.Cancelled, (string?)null);
            }

            if (s.StudentUserId is { } student && absent.Contains((s.Id, student)))
            {
                var outcome = s.AbsenceCounted switch
                {
                    true => SessionOutcomes.AbsentCounted,
                    false => SessionOutcomes.AbsentNotCounted,
                    null => SessionOutcomes.AbsentPending,
                };
                return (outcome, (string?)null);
            }

            return (s.Status == SessionStatus.Completed ? SessionOutcomes.Held : SessionOutcomes.Scheduled, (string?)null);
        });
    }

    // ---------- Who may act ----------

    private bool CanDecideAll => guard.IsStaff || guard.HasPermission(Permissions.Sessions.Manage);

    /// <summary>Staff, or the supervisor of the session's teacher.</summary>
    private async Task<bool> CanDecideAsync(Session session, CancellationToken ct) =>
        CanDecideAll
        || (guard.IsInRole(Roles.Supervisor) && (await guard.SupervisorTeacherIdsAsync(guard.Me, ct)).Contains(session.TeacherUserId));

    private async Task EnsureCanDecideAsync(Session session, CancellationToken ct)
    {
        if (!await CanDecideAsync(session, ct))
        {
            throw new ForbiddenAccessException("Only academy staff or the teacher's supervisor can decide this.");
        }
    }

    /// <summary>The student, their parent, the session's teacher, or anyone who may decide.</summary>
    private async Task EnsureCanActForStudentAsync(Session session, CancellationToken ct)
    {
        var me = guard.Me;
        var allowed = session.StudentUserId == me
                      || session.TeacherUserId == me
                      || (guard.IsInRole(Roles.Parent) && (await guard.ChildrenIdsAsync(me, ct)).Contains(session.StudentUserId!.Value))
                      || await CanDecideAsync(session, ct);
        if (!allowed)
        {
            throw new ForbiddenAccessException("You can only excuse your own (or your child's) sessions.");
        }
    }
}

internal sealed class RequestExcuseValidator : AbstractValidator<RequestExcuseRequest>
{
    public RequestExcuseValidator() => RuleFor(x => x.Reason).MaximumLength(1000);
}

internal sealed class ResolveExcuseValidator : AbstractValidator<ResolveExcuseRequest>
{
    public ResolveExcuseValidator()
    {
        RuleFor(x => x.Resolution).IsInEnum().When(x => x.Resolution.HasValue);
        RuleFor(x => x.Resolution).NotNull().When(x => !x.Reject).WithMessage("Choose what happens to the session.");
        RuleFor(x => x.MakeupStartsAtUtc).NotNull().When(x => x.Resolution == ExcuseResolution.Rescheduled)
            .WithMessage("Choose a time for the make-up session.");
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}
