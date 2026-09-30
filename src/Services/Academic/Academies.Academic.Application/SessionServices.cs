using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

/// <summary>
/// A session with its student's attendance, the supervisor's absence decision and any excuse, so
/// dashboards need one call. Every session is online and one-to-one.
/// </summary>
public sealed record SessionDto(
    long Id, string Title, long CourseId, string? CourseName, string? CourseKind,
    long TeacherUserId, string? TeacherName, DateTime StartsAtUtc, DateTime EndsAtUtc,
    string? MeetingUrl, string Status, string? Notes,
    long StudentUserId, string? StudentName, long? MakeupOfSessionId = null,
    string? AttendanceStatus = null, bool? AbsenceCounted = null, SessionExcuseDto? Excuse = null, DateTime? ReportSentOnUtc = null)
{
    public int DurationMinutes => (int)Math.Round((EndsAtUtc - StartsAtUtc).TotalMinutes);
}

public sealed record SessionExcuseDto(
    long Id, long SessionId, long StudentUserId, string? Reason, DateTime? PreferredStartsAtUtc, string Status, string? Resolution,
    long? MakeupSessionId, string? ResolutionNote, DateTime CreatedOnUtc);

public sealed record SessionQuery(
    DateTime FromUtc, DateTime ToUtc, long? TeacherUserId = null, long? StudentUserId = null, long? CourseId = null, string? Status = null);

/// <summary>
/// <see cref="RepeatWeeks"/> &gt; 1 creates the same slot weekly (US-025 weekly timetable). An empty
/// <see cref="MeetingUrl"/> puts the session in the teacher's own room.
/// </summary>
public sealed record SaveSessionRequest(
    string Title, long CourseId, long TeacherUserId, long StudentUserId, DateTime StartsAtUtc, int DurationMinutes,
    string? MeetingUrl = null, string? Notes = null, int RepeatWeeks = 1);

/// <summary>The teacher's log of a session. Every field is optional; the Quran fields apply to Quran subjects.</summary>
public sealed record SessionLogDto(
    int? Rating, string? Comment, string? Accomplished, string? Homework, string? Memorization, string? Revision, int? Mistakes);

public sealed record RosterItemDto(
    long StudentUserId, string FullName, string? AttendanceStatus, string? AttendanceNote, int? Rating, string? Comment, SessionLogDto? Log = null);

public sealed record AttendanceItem(long StudentUserId, AttendanceStatus Status, string? Note);

public sealed record RecordAttendanceRequest(IReadOnlyList<AttendanceItem> Items);

public sealed record FeedbackItem(
    long StudentUserId, int? Rating, string? Comment, string? Accomplished = null, string? Homework = null, string? Memorization = null,
    string? Revision = null, int? Mistakes = null);

public sealed record SaveFeedbackRequest(IReadOnlyList<FeedbackItem> Items);

public sealed record FeedbackDto(
    long Id, long SessionId, string SessionTitle, DateTime SessionStartsAtUtc, long StudentUserId, string? StudentName,
    long TeacherUserId, string? TeacherName, int? Rating, string? Comment, DateTime CreatedOnUtc, SessionLogDto? Log = null, string? CourseName = null);

/// <summary>A personal link into an online session; <see cref="IsModerator"/> for the teacher and staff.</summary>
public sealed record JoinLinkDto(string Url, bool IsModerator, DateTime ExpiresAtUtc);

/// <summary>A session from the student's point of view: whether they attended and the teacher's log.</summary>
public sealed record StudentSessionDto(
    SessionDto Session, string? AttendanceStatus, string? AttendanceNote, int? Rating, string? Comment, SessionLogDto? Log = null);

public interface ISessionService
{
    Task<IReadOnlyList<SessionDto>> ListAsync(SessionQuery query, CancellationToken ct = default);
    Task<SessionDto> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> CreateAsync(SaveSessionRequest request, CancellationToken ct = default);
    Task<SessionDto> UpdateAsync(long id, SaveSessionRequest request, CancellationToken ct = default);
    Task<SessionDto> CompleteAsync(long id, CancellationToken ct = default);
    Task<SessionDto> CancelAsync(long id, CancellationToken ct = default);
    Task<SessionDto> GenerateMeetingLinkAsync(long id, CancellationToken ct = default);
    Task<JoinLinkDto> JoinAsync(long id, CancellationToken ct = default);
    Task<JoinLinkDto> MyRoomAsync(CancellationToken ct = default);
    Task<JoinLinkDto> TeacherRoomLinkAsync(long teacherUserId, int days, CancellationToken ct = default);
    Task<IReadOnlyList<RosterItemDto>> RosterAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<RosterItemDto>> RecordAttendanceAsync(long id, RecordAttendanceRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RosterItemDto>> SaveFeedbackAsync(long id, SaveFeedbackRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<FeedbackDto>> StudentFeedbackAsync(long studentUserId, int take, CancellationToken ct = default);
    Task<IReadOnlyList<StudentSessionDto>> StudentSessionsAsync(long studentUserId, CancellationToken ct = default);
}

/// <summary>Scheduling (US-025), attendance (US-026), the session log (US-027) and online rooms (US-037).</summary>
internal sealed partial class SessionService(
    IAcademicDbContext db,
    AccessGuard guard,
    IMeetingLinkGenerator meetings,
    IGamificationService gamification,
    IEventPublisher events,
    TimeProvider clock) : ISessionService
{
    public async Task<IReadOnlyList<SessionDto>> ListAsync(SessionQuery query, CancellationToken ct = default)
    {
        if (query.ToUtc <= query.FromUtc || query.ToUtc - query.FromUtc > TimeSpan.FromDays(93))
        {
            throw new BusinessRuleException("Choose a date range of up to 3 months.");
        }

        var q = db.Sessions.AsNoTracking().Where(s => s.StartsAtUtc < query.ToUtc && s.EndsAtUtc > query.FromUtc);
        q = await ScopeAsync(q, ct);

        if (query.TeacherUserId is { } teacherId)
        {
            q = q.Where(s => s.TeacherUserId == teacherId);
        }

        if (query.StudentUserId is { } studentId)
        {
            await guard.EnsureCanViewStudentAsync(studentId, ct);
            q = q.Where(s => s.StudentUserId == studentId);
        }

        if (query.CourseId is { } courseId)
        {
            q = q.Where(s => s.CourseId == courseId);
        }

        if (Enum.TryParse<SessionStatus>(query.Status, true, out var status))
        {
            q = q.Where(s => s.Status == status);
        }

        return await ToDtosAsync(await q.OrderBy(s => s.StartsAtUtc).Take(1000).ToListAsync(ct), ct);
    }

    public async Task<SessionDto> GetAsync(long id, CancellationToken ct = default)
    {
        var session = await (await ScopeAsync(db.Sessions.AsNoTracking(), ct)).FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException(nameof(Session), id);
        return (await ToDtosAsync([session], ct))[0];
    }

    public async Task<IReadOnlyList<SessionDto>> CreateAsync(SaveSessionRequest request, CancellationToken ct = default)
    {
        EnsureCanSchedule(request.TeacherUserId);
        await ValidateReferencesAsync(request, ct);

        var created = new List<Session>();
        for (var week = 0; week < Math.Max(1, request.RepeatWeeks); week++)
        {
            var start = request.StartsAtUtc.AddDays(7 * week);
            var session = new Session
            {
                Title = request.Title.Trim(),
                CourseId = request.CourseId,
                StudentUserId = request.StudentUserId,
                TeacherUserId = request.TeacherUserId,
                StartsAtUtc = start,
                EndsAtUtc = start.AddMinutes(request.DurationMinutes),
                MeetingUrl = request.MeetingUrl,
                Notes = request.Notes,
            };
            await EnsureNoConflictAsync(session, ct);
            ApplyRoom(session);

            created.Add(session);
            db.Sessions.Add(session);
        }

        await db.EnsureTeacherLinkAsync(request.TeacherUserId, request.StudentUserId, ct);
        await db.EnsureEnrollmentAsync(request.StudentUserId, request.CourseId, request.TeacherUserId, Today, ct);
        await db.SaveChangesAsync(ct);
        return await ToDtosAsync(created, ct);
    }

    public async Task<SessionDto> UpdateAsync(long id, SaveSessionRequest request, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        EnsureCanSchedule(request.TeacherUserId);
        if (session.Status != SessionStatus.Scheduled)
        {
            throw new BusinessRuleException("Only scheduled sessions can be edited.");
        }

        await ValidateReferencesAsync(request, ct);
        session.Title = request.Title.Trim();
        session.CourseId = request.CourseId;
        session.StudentUserId = request.StudentUserId;
        session.TeacherUserId = request.TeacherUserId;
        session.StartsAtUtc = request.StartsAtUtc;
        session.EndsAtUtc = request.StartsAtUtc.AddMinutes(request.DurationMinutes);
        session.MeetingUrl = request.MeetingUrl;
        session.Notes = request.Notes;
        session.ReminderSentOnUtc = null;
        await EnsureNoConflictAsync(session, ct);
        ApplyRoom(session);

        await db.EnsureTeacherLinkAsync(request.TeacherUserId, request.StudentUserId, ct);
        await db.EnsureEnrollmentAsync(request.StudentUserId, request.CourseId, request.TeacherUserId, Today, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Marks the session as delivered, which is what the teacher is paid for (US-032).</summary>
    public async Task<SessionDto> CompleteAsync(long id, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        if (session.Status is SessionStatus.Cancelled or SessionStatus.Excused)
        {
            throw new BusinessRuleException("A cancelled or excused session cannot be completed.");
        }

        if (session.StartsAtUtc > clock.GetUtcNow().UtcDateTime)
        {
            throw new BusinessRuleException("A session can be completed only after it starts.");
        }

        if (session.Status != SessionStatus.Completed)
        {
            session.Status = SessionStatus.Completed;
            session.CompletedOnUtc = clock.GetUtcNow().UtcDateTime;
            await events.PublishAsync(new SessionCompleted(session.AcademyId, session.Id, session.TeacherUserId, session.StartsAtUtc), ct);
            await TrySendReportAsync(session, ct);
            await db.SaveChangesAsync(ct);
        }

        return await GetAsync(id, ct);
    }

    public async Task<SessionDto> CancelAsync(long id, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        if (session.Status == SessionStatus.Completed)
        {
            throw new BusinessRuleException("A completed session cannot be cancelled.");
        }

        session.Status = SessionStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Moves the session (back) into the teacher's own room, replacing a link typed in by hand.</summary>
    public async Task<SessionDto> GenerateMeetingLinkAsync(long id, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        session.MeetingUrl = meetings.RoomUrl(session.AcademyId, session.TeacherUserId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// The caller's own link into the session's room, signed with their name and email from the
    /// system. The session's teacher, their supervisor and staff join as moderator and can open it
    /// any time; the student (and their parent) from 30 minutes before the start until 15 minutes
    /// after the end, so a student can't wander into the next student's session in the same
    /// teacher's room. A link typed in by hand (Zoom, Meet...) is returned as is.
    /// </summary>
    public async Task<JoinLinkDto> JoinAsync(long id, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        if (session.Status is SessionStatus.Cancelled or SessionStatus.Excused)
        {
            throw new BusinessRuleException("This session isn't taking place.");
        }

        var me = guard.Me;
        var moderator = session.TeacherUserId == me || await CanDecideAsync(session, ct);
        if (!moderator)
        {
            var children = guard.IsInRole(Roles.Parent) ? await guard.ChildrenIdsAsync(me, ct) : [];
            if (session.StudentUserId != me && !children.Contains(session.StudentUserId))
            {
                throw new ForbiddenAccessException("You are not in this session.");
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var url = string.IsNullOrWhiteSpace(session.MeetingUrl) ? meetings.RoomUrl(session.AcademyId, session.TeacherUserId) : session.MeetingUrl;
        if (!meetings.IsOurRoom(url))
        {
            return new JoinLinkDto(url, moderator, session.EndsAtUtc);
        }

        var opens = session.StartsAtUtc.AddMinutes(-30);
        var closes = session.EndsAtUtc.AddMinutes(15);
        if (!moderator && (now < opens || now > closes))
        {
            throw new BusinessRuleException(now < opens
                ? "The room opens 30 minutes before the session."
                : "This session has ended.");
        }

        var expires = moderator ? (closes > now ? closes : now).AddHours(1) : closes;
        return new JoinLinkDto(await JoinUrlAsync(session.TeacherUserId, me, moderator, now, expires, ct), moderator, expires);
    }

    /// <summary>A teacher's own room, to open any time (e.g. to set it up before a session).</summary>
    public async Task<JoinLinkDto> MyRoomAsync(CancellationToken ct = default)
    {
        if (!guard.IsInRole(Roles.Teacher))
        {
            throw new ForbiddenAccessException("Only teachers have a personal room.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddHours(4);
        return new JoinLinkDto(await JoinUrlAsync(guard.Me, guard.Me, true, now, expires, ct), true, expires);
    }

    /// <summary>
    /// A link to a teacher's room for their supervisor (or staff) to send them, e.g. on WhatsApp.
    /// It opens the room as the teacher (moderator) for <paramref name="days"/> days, no login needed.
    /// </summary>
    public async Task<JoinLinkDto> TeacherRoomLinkAsync(long teacherUserId, int days, CancellationToken ct = default)
    {
        var allowed = CanDecideAll
                      || (guard.IsInRole(Roles.Supervisor) && (await guard.SupervisorTeacherIdsAsync(guard.Me, ct)).Contains(teacherUserId));
        if (!allowed)
        {
            throw new ForbiddenAccessException("Only academy staff or the teacher's supervisor can share this room.");
        }

        await db.People.EnsureRoleAsync(teacherUserId, Roles.Teacher, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddDays(Math.Clamp(days, 1, 90));
        return new JoinLinkDto(await JoinUrlAsync(teacherUserId, teacherUserId, true, now, expires, ct), true, expires);
    }

    private async Task<string> JoinUrlAsync(long teacherUserId, long me, bool moderator, DateTime now, DateTime expires, CancellationToken ct)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == me, ct);
        return meetings.JoinUrl(
            guard.AcademyId, teacherUserId, new MeetingParticipant(me, person?.FullName ?? $"#{me}", person?.Email ?? string.Empty, moderator),
            now.AddMinutes(-2), expires);
    }

    /// <summary>
    /// Sessions without a hand-typed link, or with one of our rooms, always point at the teacher's
    /// own room (so changing the teacher moves it); a link typed in by hand is kept.
    /// </summary>
    private void ApplyRoom(Session session)
    {
        if (string.IsNullOrWhiteSpace(session.MeetingUrl) || meetings.IsOurRoom(session.MeetingUrl))
        {
            session.MeetingUrl = meetings.RoomUrl(session.AcademyId == 0 ? guard.AcademyId : session.AcademyId, session.TeacherUserId);
        }
    }

    public async Task<IReadOnlyList<RosterItemDto>> RosterAsync(long id, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        return await BuildRosterAsync(session, ct);
    }

    public async Task<IReadOnlyList<RosterItemDto>> RecordAttendanceAsync(long id, RecordAttendanceRequest request, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        if (session.Status is SessionStatus.Cancelled or SessionStatus.Excused)
        {
            throw new BusinessRuleException("Attendance cannot be recorded for a cancelled or excused session.");
        }

        var unknown = request.Items.Select(i => i.StudentUserId).Where(s => s != session.StudentUserId).Distinct().ToList();
        if (unknown.Count > 0)
        {
            throw new BusinessRuleException($"Students {string.Join(", ", unknown)} are not in this session.");
        }

        var existing = await db.Attendances.Where(a => a.SessionId == id).ToListAsync(ct);
        var parent = await db.Students.Where(s => s.UserId == session.StudentUserId).Select(s => s.ParentUserId).FirstOrDefaultAsync(ct);
        var changed = new List<Attendance>();

        foreach (var item in request.Items)
        {
            var row = existing.FirstOrDefault(a => a.StudentUserId == item.StudentUserId);
            if (row is null)
            {
                row = new Attendance { SessionId = id, StudentUserId = item.StudentUserId };
                db.Attendances.Add(row);
                existing.Add(row);
            }
            else if (row.Status == item.Status && row.Note == item.Note)
            {
                continue;
            }

            row.Status = item.Status;
            row.Note = item.Note;
            changed.Add(row);
        }

        await db.SaveChangesAsync(ct);

        foreach (var row in changed)
        {
            await events.PublishAsync(
                new AttendanceRecorded(session.AcademyId, id, row.StudentUserId, row.Status.ToString(), parent, session.StartsAtUtc), ct);
            await gamification.SetAwardAsync(
                row.StudentUserId, "attendance", row.Id, GamificationRules.PointsFor(row.Status), $"Attendance: {session.Title}", ct);
        }

        await db.SaveChangesAsync(ct);
        return await BuildRosterAsync(session, ct);
    }

    /// <summary>Saves the session log. A completed session's report then goes out (once).</summary>
    public async Task<IReadOnlyList<RosterItemDto>> SaveFeedbackAsync(long id, SaveFeedbackRequest request, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        var existing = await db.Feedbacks.Where(f => f.SessionId == id).ToListAsync(ct);

        foreach (var item in request.Items)
        {
            if (item.StudentUserId != session.StudentUserId)
            {
                throw new BusinessRuleException($"Student {item.StudentUserId} is not in this session.");
            }

            var row = existing.FirstOrDefault(f => f.StudentUserId == item.StudentUserId);
            if (row is null)
            {
                row = new SessionFeedback { SessionId = id, StudentUserId = item.StudentUserId, TeacherUserId = session.TeacherUserId };
                db.Feedbacks.Add(row);
                existing.Add(row);
            }

            row.Rating = item.Rating;
            row.Comment = Clean(item.Comment);
            row.Accomplished = Clean(item.Accomplished);
            row.Homework = Clean(item.Homework);
            row.Memorization = Clean(item.Memorization);
            row.Revision = Clean(item.Revision);
            row.Mistakes = item.Mistakes;
        }

        await db.SaveChangesAsync(ct);
        if (await TrySendReportAsync(session, ct))
        {
            await db.SaveChangesAsync(ct);
        }

        return await BuildRosterAsync(session, ct);
    }

    /// <summary>
    /// Session log entries for one student (US-027). The student, their parent, their teachers and
    /// supervisors, and academy staff may see them.
    /// </summary>
    public async Task<IReadOnlyList<FeedbackDto>> StudentFeedbackAsync(long studentUserId, int take, CancellationToken ct = default)
    {
        await guard.EnsureCanViewStudentAsync(studentUserId, ct);
        var rows = await db.Feedbacks.AsNoTracking()
            .Where(f => f.StudentUserId == studentUserId)
            .Join(db.Sessions, f => f.SessionId, s => s.Id, (f, s) => new { f, s.Title, s.StartsAtUtc, s.CourseId })
            .OrderByDescending(x => x.StartsAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.SelectMany(r => new[] { r.f.StudentUserId, r.f.TeacherUserId }), ct);
        var courseIds = rows.Select(r => r.CourseId).Distinct().ToList();
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return rows.Select(r => new FeedbackDto(
            r.f.Id, r.f.SessionId, r.Title, r.StartsAtUtc, r.f.StudentUserId, names.GetValueOrDefault(r.f.StudentUserId),
            r.f.TeacherUserId, names.GetValueOrDefault(r.f.TeacherUserId), r.f.Rating, r.f.Comment, r.f.CreatedOnUtc,
            ToLog(r.f), courses.GetValueOrDefault(r.CourseId))).ToList();
    }

    /// <summary>A student's sessions, newest first, with their attendance and the teacher's log.</summary>
    public async Task<IReadOnlyList<StudentSessionDto>> StudentSessionsAsync(long studentUserId, CancellationToken ct = default)
    {
        await guard.EnsureCanViewStudentAsync(studentUserId, ct);

        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.StudentUserId == studentUserId)
            .OrderByDescending(s => s.StartsAtUtc)
            .Take(1000)
            .ToListAsync(ct);

        var sessionIds = sessions.Select(s => s.Id).ToList();
        var attendance = await db.Attendances.AsNoTracking()
            .Where(a => a.StudentUserId == studentUserId && sessionIds.Contains(a.SessionId))
            .ToDictionaryAsync(a => a.SessionId, ct);
        var feedback = await db.Feedbacks.AsNoTracking()
            .Where(f => f.StudentUserId == studentUserId && sessionIds.Contains(f.SessionId))
            .ToDictionaryAsync(f => f.SessionId, ct);

        var dtos = await ToDtosAsync(sessions, ct);
        return dtos.Select(d =>
        {
            var log = feedback.GetValueOrDefault(d.Id);
            return new StudentSessionDto(
                d, attendance.GetValueOrDefault(d.Id)?.Status.ToString(), attendance.GetValueOrDefault(d.Id)?.Note, log?.Rating, log?.Comment,
                log is null ? null : ToLog(log));
        }).ToList();
    }

    // ---- session reports ----

    /// <summary>
    /// Once a session is completed and its log is written, the report goes to the guardian (or the
    /// adult student when there is no guardian). It is sent once; later edits don't resend it.
    /// </summary>
    private async Task<bool> TrySendReportAsync(Session session, CancellationToken ct)
    {
        if (session.Status != SessionStatus.Completed || session.ReportSentOnUtc is not null)
        {
            return false;
        }

        var log = db.Feedbacks.Local.FirstOrDefault(f => f.SessionId == session.Id && f.StudentUserId == session.StudentUserId && !f.IsDeleted)
                  ?? await db.Feedbacks.AsNoTracking().FirstOrDefaultAsync(f => f.SessionId == session.Id && f.StudentUserId == session.StudentUserId, ct);
        if (log is null)
        {
            return false;
        }

        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == session.StudentUserId, ct);
        var attendance = await db.Attendances.AsNoTracking()
            .Where(a => a.SessionId == session.Id && a.StudentUserId == session.StudentUserId)
            .Select(a => (AttendanceStatus?)a.Status).FirstOrDefaultAsync(ct);
        var names = await db.People.NamesAsync([session.StudentUserId, session.TeacherUserId], ct);
        var course = await db.Courses.AsNoTracking().Where(c => c.Id == session.CourseId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        await events.PublishAsync(new SessionReportReady(
            session.AcademyId, session.Id, session.StudentUserId, names.GetValueOrDefault(session.StudentUserId, $"#{session.StudentUserId}"),
            [student?.ReportRecipient ?? session.StudentUserId], course ?? session.Title, names.GetValueOrDefault(session.TeacherUserId, string.Empty),
            session.StartsAtUtc, attendance?.ToString(), log.Rating, log.Accomplished, log.Homework, log.Memorization, log.Revision, log.Mistakes,
            log.Comment), ct);
        session.ReportSentOnUtc = clock.GetUtcNow().UtcDateTime;
        return true;
    }

    // ---- helpers ----

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    internal static SessionLogDto ToLog(SessionFeedback f) =>
        new(f.Rating, f.Comment, f.Accomplished, f.Homework, f.Memorization, f.Revision, f.Mistakes);

    /// <summary>Restricts sessions to what the caller may see (US-028).</summary>
    private async Task<IQueryable<Session>> ScopeAsync(IQueryable<Session> q, CancellationToken ct)
    {
        var teachers = await guard.VisibleTeacherIdsAsync(ct);
        if (teachers is null)
        {
            return q;
        }

        var students = await guard.VisibleStudentIdsAsync(ct) ?? [];
        return q.Where(s => teachers.Contains(s.TeacherUserId) || students.Contains(s.StudentUserId));
    }

    private void EnsureCanSchedule(long teacherUserId)
    {
        var canManage = guard.IsStaff || guard.HasPermission(Permissions.Sessions.Manage);
        if (!canManage && !(guard.IsInRole(Roles.Teacher) && teacherUserId == guard.Me))
        {
            throw new ForbiddenAccessException("Teachers can only schedule their own sessions.");
        }
    }

    private async Task ValidateReferencesAsync(SaveSessionRequest request, CancellationToken ct)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == request.CourseId, ct))
        {
            throw new NotFoundException(nameof(Course), request.CourseId);
        }

        await db.People.EnsureRoleAsync(request.StudentUserId, Roles.Student, ct);
        await db.People.EnsureRoleAsync(request.TeacherUserId, Roles.Teacher, ct);
        await db.EnsureTeacherQualifiedAsync(request.TeacherUserId, request.CourseId, ct);
    }

    /// <summary>A teacher or a student can't be in two live sessions at once; a teacher's trial sessions count too.</summary>
    private async Task EnsureNoConflictAsync(Session session, CancellationToken ct)
    {
        var clash = await db.Sessions.AsNoTracking()
            .Where(s => s.Id != session.Id && s.Status != SessionStatus.Cancelled && s.Status != SessionStatus.Excused)
            .Where(s => s.StartsAtUtc < session.EndsAtUtc && session.StartsAtUtc < s.EndsAtUtc)
            .Where(s => s.TeacherUserId == session.TeacherUserId || s.StudentUserId == session.StudentUserId)
            .FirstOrDefaultAsync(ct);

        if (clash is not null)
        {
            var who = clash.TeacherUserId == session.TeacherUserId ? "The teacher" : "The student";
            throw new ConflictException($"{who} already has '{clash.Title}' at {clash.StartsAtUtc:yyyy-MM-dd HH:mm} UTC.");
        }

        var trial = await db.Leads.AsNoTracking()
            .Where(l => l.TrialTeacherUserId == session.TeacherUserId && l.TrialStatus == TrialStatus.Scheduled)
            .Where(l => l.TrialStartsAtUtc < session.EndsAtUtc && session.StartsAtUtc < l.TrialEndsAtUtc)
            .Select(l => l.TrialStartsAtUtc)
            .FirstOrDefaultAsync(ct);
        if (trial is { } at)
        {
            throw new ConflictException($"The teacher has a trial session at {at:yyyy-MM-dd HH:mm} UTC.");
        }
    }

    private async Task<IReadOnlyList<RosterItemDto>> BuildRosterAsync(Session session, CancellationToken ct)
    {
        var id = session.StudentUserId;
        var name = (await db.People.NamesAsync([id], ct)).GetValueOrDefault(id, $"#{id}");
        var attendance = await db.Attendances.AsNoTracking().FirstOrDefaultAsync(a => a.SessionId == session.Id && a.StudentUserId == id, ct);
        var log = await db.Feedbacks.AsNoTracking().FirstOrDefaultAsync(f => f.SessionId == session.Id && f.StudentUserId == id, ct);
        return [new RosterItemDto(id, name, attendance?.Status.ToString(), attendance?.Note, log?.Rating, log?.Comment, log is null ? null : ToLog(log))];
    }

    private async Task<Session> LoadAsync(long id, CancellationToken ct) =>
        await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException(nameof(Session), id);

    internal async Task<List<SessionDto>> ToDtosAsync(IReadOnlyList<Session> sessions, CancellationToken ct)
    {
        var names = await db.People.NamesAsync(sessions.Select(s => s.TeacherUserId).Concat(sessions.Select(s => s.StudentUserId)), ct);
        var courseIds = sessions.Select(s => s.CourseId).Distinct().ToList();
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);

        // The student's attendance and the latest excuse per session.
        var ids = sessions.Select(s => s.Id).ToList();
        var attendance = ids.Count == 0 ? [] : (await db.Attendances.AsNoTracking()
                .Where(a => ids.Contains(a.SessionId))
                .Select(a => new { a.SessionId, a.StudentUserId, a.Status })
                .ToListAsync(ct))
            .Where(a => sessions.Any(s => s.Id == a.SessionId && s.StudentUserId == a.StudentUserId))
            .GroupBy(a => a.SessionId)
            .ToDictionary(g => g.Key, g => g.First().Status.ToString());
        var excuses = ids.Count == 0 ? [] : (await db.Excuses.AsNoTracking().Where(e => ids.Contains(e.SessionId)).ToListAsync(ct))
            .GroupBy(e => e.SessionId)
            .ToDictionary(g => g.Key, g => ToExcuseDto(g.OrderByDescending(e => e.Id).First()));

        return sessions.Select(s => new SessionDto(
            s.Id, s.Title, s.CourseId, courses.GetValueOrDefault(s.CourseId)?.Name, courses.GetValueOrDefault(s.CourseId)?.Kind.ToString(),
            s.TeacherUserId, names.GetValueOrDefault(s.TeacherUserId), s.StartsAtUtc, s.EndsAtUtc, s.MeetingUrl, s.Status.ToString(), s.Notes,
            s.StudentUserId, names.GetValueOrDefault(s.StudentUserId), s.MakeupOfSessionId,
            attendance.GetValueOrDefault(s.Id), s.AbsenceCounted, excuses.GetValueOrDefault(s.Id), s.ReportSentOnUtc)).ToList();
    }

    internal static SessionExcuseDto ToExcuseDto(SessionExcuse e) => new(
        e.Id, e.SessionId, e.StudentUserId, e.Reason, e.PreferredStartsAtUtc, e.Status.ToString(), e.Resolution?.ToString(),
        e.MakeupSessionId, e.ResolutionNote, e.CreatedOnUtc);
}

internal sealed class SaveSessionValidator : AbstractValidator<SaveSessionRequest>
{
    public SaveSessionValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CourseId).GreaterThan(0);
        RuleFor(x => x.TeacherUserId).GreaterThan(0);
        RuleFor(x => x.StudentUserId).GreaterThan(0).WithMessage("Choose the student.");
        RuleFor(x => x.DurationMinutes).InclusiveBetween(15, 600);
        RuleFor(x => x.RepeatWeeks).InclusiveBetween(1, 52);
        RuleFor(x => x.MeetingUrl).MaximumLength(500)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            .When(x => !string.IsNullOrWhiteSpace(x.MeetingUrl))
            .WithMessage("Enter a full http(s) link, or leave it empty to use the teacher's room.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

internal sealed class RecordAttendanceValidator : AbstractValidator<RecordAttendanceRequest>
{
    public RecordAttendanceValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(a => a.Status).IsInEnum();
            i.RuleFor(a => a.Note).MaximumLength(300);
        });
    }
}

internal sealed class SaveFeedbackValidator : AbstractValidator<SaveFeedbackRequest>
{
    public SaveFeedbackValidator()
    {
        RuleFor(x => x.Items).NotEmpty();
        RuleForEach(x => x.Items).ChildRules(i =>
        {
            i.RuleFor(f => f.Rating).InclusiveBetween(1, 5).When(f => f.Rating.HasValue);
            i.RuleFor(f => f.Comment).MaximumLength(1000);
            i.RuleFor(f => f.Accomplished).MaximumLength(2000);
            i.RuleFor(f => f.Homework).MaximumLength(1000);
            i.RuleFor(f => f.Memorization).MaximumLength(300);
            i.RuleFor(f => f.Revision).MaximumLength(300);
            i.RuleFor(f => f.Mistakes).InclusiveBetween(0, 1000).When(f => f.Mistakes.HasValue);
            i.RuleFor(f => f).Must(f => f.Rating.HasValue || f.Mistakes.HasValue
                                        || new[] { f.Comment, f.Accomplished, f.Homework, f.Memorization, f.Revision }.Any(t => !string.IsNullOrWhiteSpace(t)))
                .WithMessage("Write at least one thing in the session log.");
        });
    }
}
