using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

public sealed record SessionDto(
    long Id, string Title, long CourseId, string? CourseName, long? GroupId, string? GroupName,
    long TeacherUserId, string? TeacherName, DateTime StartsAtUtc, DateTime EndsAtUtc, string Type,
    string? MeetingUrl, string? Location, string Status, string? Notes);

public sealed record SessionQuery(
    DateTime FromUtc, DateTime ToUtc, long? TeacherUserId = null, long? GroupId = null, long? StudentUserId = null, string? Status = null);

/// <summary><see cref="RepeatWeeks"/> &gt; 1 creates the same slot weekly (US-025 weekly timetable).</summary>
public sealed record SaveSessionRequest(
    string Title, long CourseId, long? GroupId, long TeacherUserId, DateTime StartsAtUtc, int DurationMinutes,
    SessionType Type, string? Location, string? MeetingUrl, bool GenerateMeetingLink, string? Notes, int RepeatWeeks = 1);

public sealed record RosterItemDto(
    long StudentUserId, string FullName, string? AttendanceStatus, string? AttendanceNote, int? Rating, string? Comment);

public sealed record AttendanceItem(long StudentUserId, AttendanceStatus Status, string? Note);

public sealed record RecordAttendanceRequest(IReadOnlyList<AttendanceItem> Items);

public sealed record FeedbackItem(long StudentUserId, int Rating, string? Comment);

public sealed record SaveFeedbackRequest(IReadOnlyList<FeedbackItem> Items);

public sealed record FeedbackDto(
    long Id, long SessionId, string SessionTitle, DateTime SessionStartsAtUtc, long StudentUserId, string? StudentName,
    long TeacherUserId, string? TeacherName, int Rating, string? Comment, DateTime CreatedOnUtc);

public interface ISessionService
{
    Task<IReadOnlyList<SessionDto>> ListAsync(SessionQuery query, CancellationToken ct = default);
    Task<SessionDto> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> CreateAsync(SaveSessionRequest request, CancellationToken ct = default);
    Task<SessionDto> UpdateAsync(long id, SaveSessionRequest request, CancellationToken ct = default);
    Task<SessionDto> CompleteAsync(long id, CancellationToken ct = default);
    Task<SessionDto> CancelAsync(long id, CancellationToken ct = default);
    Task<SessionDto> GenerateMeetingLinkAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<RosterItemDto>> RosterAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<RosterItemDto>> RecordAttendanceAsync(long id, RecordAttendanceRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<RosterItemDto>> SaveFeedbackAsync(long id, SaveFeedbackRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<FeedbackDto>> StudentFeedbackAsync(long studentUserId, int take, CancellationToken ct = default);
}

/// <summary>Scheduling (US-025), attendance (US-026), feedback (US-027) and online links (US-037).</summary>
internal sealed class SessionService(
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
            q = q.Where(s => s.GroupId != null && db.GroupStudents.Any(gs => gs.GroupId == s.GroupId && gs.StudentUserId == studentId));
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
                TeacherUserId = request.TeacherUserId,
                StartsAtUtc = start,
                EndsAtUtc = start.AddMinutes(request.DurationMinutes),
                Type = request.Type,
                Location = request.Location,
                MeetingUrl = request.MeetingUrl,
                Notes = request.Notes,
            };
            await EnsureNoConflictAsync(session, ct);
            if (session.Type == SessionType.Online && request.GenerateMeetingLink && string.IsNullOrWhiteSpace(session.MeetingUrl))
            {
                session.MeetingUrl = meetings.Generate(guard.AcademyId, session.Title, session.StartsAtUtc);
            }

            created.Add(session);
            db.Sessions.Add(session);
        }

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
        session.TeacherUserId = request.TeacherUserId;
        session.StartsAtUtc = request.StartsAtUtc;
        session.EndsAtUtc = request.StartsAtUtc.AddMinutes(request.DurationMinutes);
        session.Type = request.Type;
        session.Location = request.Location;
        session.MeetingUrl = request.MeetingUrl;
        session.Notes = request.Notes;
        session.ReminderSentOnUtc = null;
        await EnsureNoConflictAsync(session, ct);
        if (session.Type == SessionType.Online && request.GenerateMeetingLink && string.IsNullOrWhiteSpace(session.MeetingUrl))
        {
            session.MeetingUrl = meetings.Generate(guard.AcademyId, session.Title, session.StartsAtUtc);
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Marks the session as delivered, which is what the teacher is paid for (US-032).</summary>
    public async Task<SessionDto> CompleteAsync(long id, CancellationToken ct = default)
    {
        var session = await LoadAsync(id, ct);
        guard.EnsureCanRunSession(session);
        if (session.Status == SessionStatus.Cancelled)
        {
            throw new BusinessRuleException("A cancelled session cannot be completed.");
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
        session.MeetingUrl = meetings.Generate(session.AcademyId, session.Title, session.StartsAtUtc);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
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
        if (session.Status == SessionStatus.Cancelled)
        {
            throw new BusinessRuleException("Attendance cannot be recorded for a cancelled session.");
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
                            || (s.GroupId != null && db.GroupStudents.Any(gs => gs.GroupId == s.GroupId && students.Contains(gs.StudentUserId))));
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

        await db.People.EnsureRoleAsync(request.TeacherUserId, Roles.Teacher, ct);
        if (request.Type == SessionType.Online)
        {
            await entitlements.EnsureFeatureAsync(guard.AcademyId, FeatureKeys.OnlineSessions, ct);
        }
    }

    /// <summary>A teacher (or a group) can't be in two scheduled sessions at once.</summary>
    private async Task EnsureNoConflictAsync(Session session, CancellationToken ct)
    {
        var clash = await db.Sessions.AsNoTracking()
            .Where(s => s.Id != session.Id && s.Status != SessionStatus.Cancelled)
            .Where(s => s.StartsAtUtc < session.EndsAtUtc && session.StartsAtUtc < s.EndsAtUtc)
            .Where(s => s.TeacherUserId == session.TeacherUserId || (session.GroupId != null && s.GroupId == session.GroupId))
            .FirstOrDefaultAsync(ct);

        if (clash is not null)
        {
            var who = clash.TeacherUserId == session.TeacherUserId ? "The teacher" : "The group";
            throw new ConflictException($"{who} already has '{clash.Title}' at {clash.StartsAtUtc:yyyy-MM-dd HH:mm} UTC.");
        }
    }

    private async Task<List<long>> RosterStudentIdsAsync(Session session, CancellationToken ct)
    {
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

    private async Task<List<SessionDto>> ToDtosAsync(IReadOnlyList<Session> sessions, CancellationToken ct)
    {
        var names = await db.People.NamesAsync(sessions.Select(s => s.TeacherUserId), ct);
        var courseIds = sessions.Select(s => s.CourseId).Distinct().ToList();
        var groupIds = sessions.Where(s => s.GroupId.HasValue).Select(s => s.GroupId!.Value).Distinct().ToList();
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var groups = await db.Groups.Where(g => groupIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, g => g.Name, ct);

        return sessions.Select(s => new SessionDto(
            s.Id, s.Title, s.CourseId, courses.GetValueOrDefault(s.CourseId), s.GroupId,
            s.GroupId is { } g ? groups.GetValueOrDefault(g) : null, s.TeacherUserId, names.GetValueOrDefault(s.TeacherUserId),
            s.StartsAtUtc, s.EndsAtUtc, s.Type.ToString(), s.MeetingUrl, s.Location, s.Status.ToString(), s.Notes)).ToList();
    }
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
