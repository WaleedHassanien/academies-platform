using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

/// <summary>
/// For one-to-one sessions (<see cref="StudentUserId"/> set) it also carries that student's
/// attendance, the supervisor's absence decision and any excuse, so dashboards need one call.
/// </summary>
public sealed record SessionDto(
    long Id, string Title, long CourseId, string? CourseName, long? GroupId, string? GroupName,
    long TeacherUserId, string? TeacherName, DateTime StartsAtUtc, DateTime EndsAtUtc, string Type,
    string? MeetingUrl, string? Location, string Status, string? Notes,
    long? StudentUserId = null, string? StudentName = null, long? MakeupOfSessionId = null,
    string? AttendanceStatus = null, bool? AbsenceCounted = null, SessionExcuseDto? Excuse = null)
{
    public int DurationMinutes => (int)Math.Round((EndsAtUtc - StartsAtUtc).TotalMinutes);
}

public sealed record SessionExcuseDto(
    long Id, long SessionId, long StudentUserId, string? Reason, DateTime? PreferredStartsAtUtc, string Status, string? Resolution,
    long? MakeupSessionId, string? ResolutionNote, DateTime CreatedOnUtc);

public sealed record SessionQuery(
    DateTime FromUtc, DateTime ToUtc, long? TeacherUserId = null, long? GroupId = null, long? StudentUserId = null, string? Status = null);

/// <summary>
/// <see cref="RepeatWeeks"/> &gt; 1 creates the same slot weekly (US-025 weekly timetable).
/// <see cref="StudentUserId"/> makes it a one-to-one session (then <see cref="GroupId"/> must be empty).
/// </summary>
public sealed record SaveSessionRequest(
    string Title, long CourseId, long? GroupId, long TeacherUserId, DateTime StartsAtUtc, int DurationMinutes,
    SessionType Type, string? Location, string? MeetingUrl, bool GenerateMeetingLink, string? Notes, int RepeatWeeks = 1,
    long? StudentUserId = null);

public sealed record RosterItemDto(
    long StudentUserId, string FullName, string? AttendanceStatus, string? AttendanceNote, int? Rating, string? Comment);

public sealed record AttendanceItem(long StudentUserId, AttendanceStatus Status, string? Note);

public sealed record RecordAttendanceRequest(IReadOnlyList<AttendanceItem> Items);

public sealed record FeedbackItem(long StudentUserId, int Rating, string? Comment);

public sealed record SaveFeedbackRequest(IReadOnlyList<FeedbackItem> Items);

public sealed record FeedbackDto(
    long Id, long SessionId, string SessionTitle, DateTime SessionStartsAtUtc, long StudentUserId, string? StudentName,
    long TeacherUserId, string? TeacherName, int Rating, string? Comment, DateTime CreatedOnUtc);

/// <summary>A personal link into an online session; <see cref="IsModerator"/> for the teacher and staff.</summary>
public sealed record JoinLinkDto(string Url, bool IsModerator, DateTime ExpiresAtUtc);

/// <summary>A session from one student's point of view: whether they attended and what the teacher said.</summary>
public sealed record StudentSessionDto(SessionDto Session, string? AttendanceStatus, string? AttendanceNote, int? Rating, string? Comment);

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

/// <summary>Scheduling (US-025), attendance (US-026), feedback (US-027) and online links (US-037).</summary>
internal sealed partial class SessionService(
    IAcademicDbContext db,
    AccessGuard guard,
    IEntitlementsProvider entitlements,
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

        if (query.GroupId is { } groupId)
        {
            q = q.Where(s => s.GroupId == groupId);
        }

        if (query.StudentUserId is { } studentId)
        {
            await guard.EnsureCanViewStudentAsync(studentId, ct);
            q = q.Where(s => s.StudentUserId == studentId
                             || (s.GroupId != null && db.GroupStudents.Any(gs => gs.GroupId == s.GroupId && gs.StudentUserId == studentId)));
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
                GroupId = request.GroupId,
                StudentUserId = request.StudentUserId,
                TeacherUserId = request.TeacherUserId,
                StartsAtUtc = start,
                EndsAtUtc = start.AddMinutes(request.DurationMinutes),
                Type = request.Type,
                Location = request.Location,
                MeetingUrl = request.MeetingUrl,
                Notes = request.Notes,
            };
            await EnsureNoConflictAsync(session, ct);
            ApplyRoom(session, request.GenerateMeetingLink);

            created.Add(session);
            db.Sessions.Add(session);
        }

        await EnsureTeacherLinkAsync(request.TeacherUserId, request.StudentUserId, ct);
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
        session.GroupId = request.GroupId;
        session.StudentUserId = request.StudentUserId;
        session.TeacherUserId = request.TeacherUserId;
        session.StartsAtUtc = request.StartsAtUtc;
        session.EndsAtUtc = request.StartsAtUtc.AddMinutes(request.DurationMinutes);
        session.Type = request.Type;
        session.Location = request.Location;
        session.MeetingUrl = request.MeetingUrl;
        session.Notes = request.Notes;
        session.ReminderSentOnUtc = null;
        await EnsureNoConflictAsync(session, ct);
        ApplyRoom(session, request.GenerateMeetingLink);

        await EnsureTeacherLinkAsync(request.TeacherUserId, request.StudentUserId, ct);
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

    public async Task<SessionDto> GenerateMeetingLinkAsync(long id, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        await entitlements.EnsureFeatureAsync(session.AcademyId, FeatureKeys.OnlineSessions, ct);
        session.Type = SessionType.Online;
        session.MeetingUrl = meetings.RoomUrl(session.AcademyId, session.TeacherUserId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// The caller's own link into the session's room, signed with their name and email from the
    /// system. The session's teacher, their supervisor and staff join as moderator and can open it
    /// any time; the students on the roster (and their parents) from 30 minutes before the start
    /// until 15 minutes after the end, so a student can't wander into the next student's session
    /// in the same teacher's room. A link typed in by hand (Zoom, Meet...) is returned as is.
    /// </summary>
    public async Task<JoinLinkDto> JoinAsync(long id, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        if (session.Type != SessionType.Online || string.IsNullOrWhiteSpace(session.MeetingUrl))
        {
            throw new BusinessRuleException("This session has no online room.");
        }

        if (session.Status is SessionStatus.Cancelled or SessionStatus.Excused)
        {
            throw new BusinessRuleException("This session isn't taking place.");
        }

        var me = guard.Me;
        var moderator = session.TeacherUserId == me || await CanDecideAsync(session, ct);
        if (!moderator)
        {
            var roster = await RosterStudentIdsAsync(session, ct);
            var children = guard.IsInRole(Roles.Parent) ? await guard.ChildrenIdsAsync(me, ct) : [];
            if (!roster.Contains(me) && !roster.Intersect(children).Any())
            {
                throw new ForbiddenAccessException("You are not in this session.");
            }
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (!meetings.IsOurRoom(session.MeetingUrl))
        {
            return new JoinLinkDto(session.MeetingUrl, moderator, session.EndsAtUtc);
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
    /// Online sessions in our rooms always point at the teacher's own room (so changing the teacher
    /// moves it); a link typed in by hand is kept.
    /// </summary>
    private void ApplyRoom(Session session, bool generate)
    {
        if (session.Type != SessionType.Online)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.MeetingUrl) ? generate : meetings.IsOurRoom(session.MeetingUrl))
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

        var roster = await RosterStudentIdsAsync(session, ct);
        var unknown = request.Items.Select(i => i.StudentUserId).Except(roster).ToList();
        if (unknown.Count > 0)
        {
            throw new BusinessRuleException($"Students {string.Join(", ", unknown)} are not in this session's roster.");
        }

        var existing = await db.Attendances.Where(a => a.SessionId == id).ToListAsync(ct);
        var parents = await db.Students.Where(s => roster.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, s => s.ParentUserId, ct);
        var changed = new List<Attendance>();

        foreach (var item in request.Items)
        {
            var row = existing.FirstOrDefault(a => a.StudentUserId == item.StudentUserId);
            if (row is null)
            {
                row = new Attendance { SessionId = id, StudentUserId = item.StudentUserId };
                db.Attendances.Add(row);
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
                new AttendanceRecorded(session.AcademyId, id, row.StudentUserId, row.Status.ToString(), parents.GetValueOrDefault(row.StudentUserId), session.StartsAtUtc),
                ct);
            await gamification.SetAwardAsync(
                row.StudentUserId, "attendance", row.Id, GamificationRules.PointsFor(row.Status), $"Attendance: {session.Title}", ct);
        }

        await db.SaveChangesAsync(ct);
        return await BuildRosterAsync(session, ct);
    }

    public async Task<IReadOnlyList<RosterItemDto>> SaveFeedbackAsync(long id, SaveFeedbackRequest request, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        var roster = await RosterStudentIdsAsync(session, ct);
        var existing = await db.Feedbacks.Where(f => f.SessionId == id).ToListAsync(ct);

        foreach (var item in request.Items)
        {
            if (!roster.Contains(item.StudentUserId))
            {
                throw new BusinessRuleException($"Student {item.StudentUserId} is not in this session's roster.");
            }

            var row = existing.FirstOrDefault(f => f.StudentUserId == item.StudentUserId);
            if (row is null)
            {
                row = new SessionFeedback { SessionId = id, StudentUserId = item.StudentUserId, TeacherUserId = session.TeacherUserId };
                db.Feedbacks.Add(row);
            }

            row.Rating = item.Rating;
            row.Comment = item.Comment;
        }

        await db.SaveChangesAsync(ct);
        return await BuildRosterAsync(session, ct);
    }

    /// <summary>
    /// Feedback for one student (US-027). The student, their parent, their teachers and
    /// supervisors, and academy staff may see it.
    /// </summary>
    public async Task<IReadOnlyList<FeedbackDto>> StudentFeedbackAsync(long studentUserId, int take, CancellationToken ct = default)
    {
        await guard.EnsureCanViewStudentAsync(studentUserId, ct);
        var rows = await db.Feedbacks.AsNoTracking()
            .Where(f => f.StudentUserId == studentUserId)
            .Join(db.Sessions, f => f.SessionId, s => s.Id, (f, s) => new { f, s.Title, s.StartsAtUtc })
            .OrderByDescending(x => x.StartsAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.SelectMany(r => new[] { r.f.StudentUserId, r.f.TeacherUserId }), ct);

        return rows.Select(r => new FeedbackDto(
            r.f.Id, r.f.SessionId, r.Title, r.StartsAtUtc, r.f.StudentUserId, names.GetValueOrDefault(r.f.StudentUserId),
            r.f.TeacherUserId, names.GetValueOrDefault(r.f.TeacherUserId), r.f.Rating, r.f.Comment, r.f.CreatedOnUtc)).ToList();
    }

    /// <summary>
    /// A student's sessions, newest first: those of their groups, the one-to-one sessions of
    /// their own teachers (the same rule as the roster), and any session they have attendance in.
    /// </summary>
    public async Task<IReadOnlyList<StudentSessionDto>> StudentSessionsAsync(long studentUserId, CancellationToken ct = default)
    {
        await guard.EnsureCanViewStudentAsync(studentUserId, ct);

        var groupIds = db.GroupStudents.Where(gs => gs.StudentUserId == studentUserId).Select(gs => gs.GroupId);
        var teacherIds = db.TeacherStudents.Where(t => t.StudentUserId == studentUserId).Select(t => t.TeacherUserId);
        var attended = db.Attendances.Where(a => a.StudentUserId == studentUserId).Select(a => a.SessionId);

        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.StudentUserId == studentUserId
                        || (s.GroupId != null && groupIds.Contains(s.GroupId.Value))
                        || (s.GroupId == null && s.StudentUserId == null && teacherIds.Contains(s.TeacherUserId))
                        || attended.Contains(s.Id))
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
        return dtos.Select(d => new StudentSessionDto(
            d,
            attendance.GetValueOrDefault(d.Id)?.Status.ToString(), attendance.GetValueOrDefault(d.Id)?.Note,
            feedback.GetValueOrDefault(d.Id)?.Rating, feedback.GetValueOrDefault(d.Id)?.Comment)).ToList();
    }

    // ---- helpers ----

    /// <summary>Restricts sessions to what the caller may see (US-028).</summary>
    private async Task<IQueryable<Session>> ScopeAsync(IQueryable<Session> q, CancellationToken ct)
    {
        var teachers = await guard.VisibleTeacherIdsAsync(ct);
        if (teachers is null)
        {
            return q;
        }

        var students = await guard.VisibleStudentIdsAsync(ct) ?? [];
        return q.Where(s => teachers.Contains(s.TeacherUserId)
                            || (s.StudentUserId != null && students.Contains(s.StudentUserId.Value))
                            || (s.GroupId != null && db.GroupStudents.Any(gs => gs.GroupId == s.GroupId && students.Contains(gs.StudentUserId))));
    }

    /// <summary>Scheduling a one-to-one session makes the teacher responsible for the student, if they weren't already.</summary>
    private async Task EnsureTeacherLinkAsync(long teacherUserId, long? studentUserId, CancellationToken ct)
    {
        if (studentUserId is { } studentId
            && !await db.TeacherStudents.AnyAsync(t => t.TeacherUserId == teacherUserId && t.StudentUserId == studentId, ct))
        {
            db.TeacherStudents.Add(new TeacherStudent { TeacherUserId = teacherUserId, StudentUserId = studentId });
        }
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

        if (request.GroupId is { } groupId && !await db.Groups.AnyAsync(g => g.Id == groupId, ct))
        {
            throw new NotFoundException(nameof(Group), groupId);
        }

        if (request.StudentUserId is { } studentId)
        {
            if (request.GroupId is not null)
            {
                throw new BusinessRuleException("A one-to-one session can't also belong to a group.");
            }

            await db.People.EnsureRoleAsync(studentId, Roles.Student, ct);
        }

        await db.People.EnsureRoleAsync(request.TeacherUserId, Roles.Teacher, ct);
        if (request.Type == SessionType.Online)
        {
            await entitlements.EnsureFeatureAsync(guard.AcademyId, FeatureKeys.OnlineSessions, ct);
        }
    }

    /// <summary>A teacher, a group or a one-to-one student can't be in two live sessions at once.</summary>
    private async Task EnsureNoConflictAsync(Session session, CancellationToken ct)
    {
        var clash = await db.Sessions.AsNoTracking()
            .Where(s => s.Id != session.Id && s.Status != SessionStatus.Cancelled && s.Status != SessionStatus.Excused)
            .Where(s => s.StartsAtUtc < session.EndsAtUtc && session.StartsAtUtc < s.EndsAtUtc)
            .Where(s => s.TeacherUserId == session.TeacherUserId
                        || (session.GroupId != null && s.GroupId == session.GroupId)
                        || (session.StudentUserId != null && s.StudentUserId == session.StudentUserId))
            .FirstOrDefaultAsync(ct);

        if (clash is not null)
        {
            var who = clash.TeacherUserId == session.TeacherUserId ? "The teacher"
                : session.StudentUserId != null && clash.StudentUserId == session.StudentUserId ? "The student"
                : "The group";
            throw new ConflictException($"{who} already has '{clash.Title}' at {clash.StartsAtUtc:yyyy-MM-dd HH:mm} UTC.");
        }
    }

    private async Task<List<long>> RosterStudentIdsAsync(Session session, CancellationToken ct)
    {
        if (session.StudentUserId is { } only)
        {
            return [only];
        }

        var fromGroup = session.GroupId is { } groupId
            ? await db.GroupStudents.Where(gs => gs.GroupId == groupId).Select(gs => gs.StudentUserId).ToListAsync(ct)
            : await db.TeacherStudents.Where(t => t.TeacherUserId == session.TeacherUserId).Select(t => t.StudentUserId).ToListAsync(ct);
        var recorded = await db.Attendances.Where(a => a.SessionId == session.Id).Select(a => a.StudentUserId).ToListAsync(ct);
        return fromGroup.Union(recorded).ToList();
    }

    private async Task<IReadOnlyList<RosterItemDto>> BuildRosterAsync(Session session, CancellationToken ct)
    {
        var ids = await RosterStudentIdsAsync(session, ct);
        var names = await db.People.NamesAsync(ids, ct);
        var attendance = await db.Attendances.Where(a => a.SessionId == session.Id).ToDictionaryAsync(a => a.StudentUserId, ct);
        var feedback = await db.Feedbacks.Where(f => f.SessionId == session.Id).ToDictionaryAsync(f => f.StudentUserId, ct);

        return ids.Select(id => new RosterItemDto(
                id, names.GetValueOrDefault(id, $"#{id}"),
                attendance.GetValueOrDefault(id)?.Status.ToString(), attendance.GetValueOrDefault(id)?.Note,
                feedback.GetValueOrDefault(id)?.Rating, feedback.GetValueOrDefault(id)?.Comment))
            .OrderBy(r => r.FullName)
            .ToList();
    }

    private async Task<Session> LoadAsync(long id, CancellationToken ct) =>
        await db.Sessions.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException(nameof(Session), id);

    internal async Task<List<SessionDto>> ToDtosAsync(IReadOnlyList<Session> sessions, CancellationToken ct)
    {
        var names = await db.People.NamesAsync(
            sessions.Select(s => s.TeacherUserId).Concat(sessions.Where(s => s.StudentUserId.HasValue).Select(s => s.StudentUserId!.Value)), ct);
        var courseIds = sessions.Select(s => s.CourseId).Distinct().ToList();
        var groupIds = sessions.Where(s => s.GroupId.HasValue).Select(s => s.GroupId!.Value).Distinct().ToList();
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var groups = await db.Groups.Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        // One-to-one extras: the student's attendance and the latest excuse per session.
        var soloIds = sessions.Where(s => s.StudentUserId.HasValue).Select(s => s.Id).ToList();
        var attendance = soloIds.Count == 0 ? [] : (await db.Attendances.AsNoTracking()
                .Where(a => soloIds.Contains(a.SessionId))
                .Select(a => new { a.SessionId, a.StudentUserId, a.Status })
                .ToListAsync(ct))
            .Where(a => sessions.Any(s => s.Id == a.SessionId && s.StudentUserId == a.StudentUserId))
            .ToDictionary(a => a.SessionId, a => a.Status.ToString());
        var excuses = soloIds.Count == 0 ? [] : (await db.Excuses.AsNoTracking().Where(e => soloIds.Contains(e.SessionId)).ToListAsync(ct))
            .GroupBy(e => e.SessionId)
            .ToDictionary(g => g.Key, g => ToExcuseDto(g.OrderByDescending(e => e.Id).First()));

        return sessions.Select(s => new SessionDto(
            s.Id, s.Title, s.CourseId, courses.GetValueOrDefault(s.CourseId), s.GroupId,
            s.GroupId is { } g ? groups.GetValueOrDefault(g) : null, s.TeacherUserId, names.GetValueOrDefault(s.TeacherUserId),
            s.StartsAtUtc, s.EndsAtUtc, s.Type.ToString(), s.MeetingUrl, s.Location, s.Status.ToString(), s.Notes,
            s.StudentUserId, s.StudentUserId is { } sid ? names.GetValueOrDefault(sid) : null, s.MakeupOfSessionId,
            attendance.GetValueOrDefault(s.Id), s.AbsenceCounted, excuses.GetValueOrDefault(s.Id))).ToList();
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
        RuleFor(x => x.DurationMinutes).InclusiveBetween(15, 600);
        RuleFor(x => x.RepeatWeeks).InclusiveBetween(1, 52);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Location).MaximumLength(200);
        RuleFor(x => x.MeetingUrl).MaximumLength(500);
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
            i.RuleFor(f => f.Rating).InclusiveBetween(1, 5);
            i.RuleFor(f => f.Comment).MaximumLength(1000);
        });
    }
}
