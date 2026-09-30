using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

public sealed record AttendanceSummaryDto(int Present, int Late, int Absent)
{
    public int Total => Present + Late + Absent;
    public double Rate => Total == 0 ? 0 : Math.Round((Present + Late) * 100.0 / Total, 1);
}

/// <summary><see cref="Trials"/>: the teacher's upcoming and not-yet-assessed trial sessions with prospective students.</summary>
public sealed record TeacherOverviewDto(
    IReadOnlyList<PersonRefDto> Students, IReadOnlyList<SessionDto> Upcoming, int PendingToComplete, int CompletedThisMonth,
    IReadOnlyList<TrialDto>? Trials = null);

public sealed record SupervisedTeacherDto(long UserId, string FullName, int Students, int CompletedThisMonth);

public sealed record SupervisorOverviewDto(IReadOnlyList<SupervisedTeacherDto> Teachers, IReadOnlyList<SessionDto> Upcoming);

public sealed record StudentOverviewDto(
    long UserId, string FullName, string? Level, AttendanceSummaryDto Attendance, IReadOnlyList<SessionDto> Upcoming,
    IReadOnlyList<FeedbackDto> RecentFeedback, int Points);

/// <summary>What each role sees on its dashboard (US-028). Parents get one entry per child (US-040).</summary>
public sealed record MyOverviewDto(
    TeacherOverviewDto? Teacher, SupervisorOverviewDto? Supervisor, StudentOverviewDto? Student, IReadOnlyList<StudentOverviewDto>? Children);

public sealed record TeacherSessionCountDto(long TeacherUserId, int CompletedSessions);

public sealed record MonthlyAcademicDto(string Month, int SessionsCompleted, double AttendanceRate);

public sealed record AcademicStatsDto(
    int ActiveStudents, int Teachers, int SessionsScheduled, int SessionsCompleted, int SessionsCancelled,
    double AttendanceRate, IReadOnlyList<MonthlyAcademicDto> Monthly, IReadOnlyList<TeacherSessionsDto> TopTeachers);

public sealed record TeacherSessionsDto(long UserId, string FullName, int Sessions);

public interface IOverviewService
{
    Task<MyOverviewDto> MineAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TeacherSessionCountDto>> CompletedSessionCountsAsync(int year, int month, CancellationToken ct = default);
    Task<AcademicStatsDto> StatsAsync(DateTime fromUtc, DateTime toUtc, long? supervisorUserId, CancellationToken ct = default);
}

internal sealed class OverviewService(
    IAcademicDbContext db, AccessGuard guard, ISessionService sessions, ILeadService leads, TimeProvider clock) : IOverviewService
{
    public async Task<MyOverviewDto> MineAsync(CancellationToken ct = default)
    {
        var me = guard.Me;
        var now = clock.GetUtcNow().UtcDateTime;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        TeacherOverviewDto? teacher = null;
        if (guard.IsInRole(Roles.Teacher))
        {
            var studentIds = await guard.TeacherStudentIdsAsync(me, ct);
            var names = await db.People.NamesAsync(studentIds, ct);
            var upcoming = await sessions.ListAsync(new SessionQuery(now, now.AddDays(7), TeacherUserId: me, Status: nameof(SessionStatus.Scheduled)), ct);
            var pending = await db.Sessions.CountAsync(s => s.TeacherUserId == me && s.Status == SessionStatus.Scheduled && s.EndsAtUtc < now, ct);
            var completed = await db.Sessions.CountAsync(s => s.TeacherUserId == me && s.Status == SessionStatus.Completed && s.StartsAtUtc >= monthStart, ct);
            teacher = new TeacherOverviewDto(
                names.Select(n => new PersonRefDto(n.Key, n.Value)).OrderBy(n => n.FullName).ToList(), upcoming, pending, completed,
                await leads.MyTrialsAsync(ct));
        }

        SupervisorOverviewDto? supervisor = null;
        if (guard.IsInRole(Roles.Supervisor))
        {
            var teacherIds = await guard.SupervisorTeacherIdsAsync(me, ct);
            var names = await db.People.NamesAsync(teacherIds, ct);
            var rows = new List<SupervisedTeacherDto>();
            foreach (var id in teacherIds)
            {
                var students = (await guard.TeacherStudentIdsAsync(id, ct)).Count;
                var completed = await db.Sessions.CountAsync(s => s.TeacherUserId == id && s.Status == SessionStatus.Completed && s.StartsAtUtc >= monthStart, ct);
                rows.Add(new SupervisedTeacherDto(id, names.GetValueOrDefault(id, $"#{id}"), students, completed));
            }

            var upcoming = (await sessions.ListAsync(new SessionQuery(now, now.AddDays(7), Status: nameof(SessionStatus.Scheduled)), ct))
                .Where(s => teacherIds.Contains(s.TeacherUserId)).ToList();
            supervisor = new SupervisorOverviewDto(rows.OrderBy(r => r.FullName).ToList(), upcoming);
        }

        var student = guard.IsInRole(Roles.Student) ? await StudentOverviewAsync(me, now, ct) : null;

        List<StudentOverviewDto>? children = null;
        if (guard.IsInRole(Roles.Parent))
        {
            children = [];
            foreach (var childId in await guard.ChildrenIdsAsync(me, ct))
            {
                children.Add(await StudentOverviewAsync(childId, now, ct));
            }
        }

        return new MyOverviewDto(teacher, supervisor, student, children);
    }

    /// <summary>Completed sessions per teacher in a month, for salaries (US-032).</summary>
    public async Task<IReadOnlyList<TeacherSessionCountDto>> CompletedSessionCountsAsync(int year, int month, CancellationToken ct = default)
    {
        var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = from.AddMonths(1);
        return await db.Sessions
            .Where(s => s.Status == SessionStatus.Completed && s.StartsAtUtc >= from && s.StartsAtUtc < to)
            .GroupBy(s => s.TeacherUserId)
            .Select(g => new TeacherSessionCountDto(g.Key, g.Count()))
            .ToListAsync(ct);
    }

    /// <summary>Academic KPIs for dashboards (US-038). A supervisor's view is limited to their teachers.</summary>
    public async Task<AcademicStatsDto> StatsAsync(DateTime fromUtc, DateTime toUtc, long? supervisorUserId, CancellationToken ct = default)
    {
        List<long>? teacherIds = supervisorUserId is { } sup ? await guard.SupervisorTeacherIdsAsync(sup, ct) : null;

        var sessionsQ = db.Sessions.Where(s => s.StartsAtUtc >= fromUtc && s.StartsAtUtc < toUtc);
        if (teacherIds is not null)
        {
            sessionsQ = sessionsQ.Where(s => teacherIds.Contains(s.TeacherUserId));
        }

        var statusCounts = await sessionsQ.GroupBy(s => s.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var attendanceQ = db.Attendances.Join(sessionsQ, a => a.SessionId, s => s.Id, (a, s) => new { a.Status, s.StartsAtUtc });
        var attendance = await attendanceQ.ToListAsync(ct);
        var completed = await sessionsQ.Where(s => s.Status == SessionStatus.Completed).Select(s => new { s.StartsAtUtc, s.TeacherUserId }).ToListAsync(ct);

        var monthly = new List<MonthlyAcademicDto>();
        for (var m = new DateTime(fromUtc.Year, fromUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc); m < toUtc; m = m.AddMonths(1))
        {
            var next = m.AddMonths(1);
            var inMonth = attendance.Where(a => a.StartsAtUtc >= m && a.StartsAtUtc < next).ToList();
            monthly.Add(new MonthlyAcademicDto(
                m.ToString("yyyy-MM"),
                completed.Count(s => s.StartsAtUtc >= m && s.StartsAtUtc < next),
                Rate(inMonth.Count(a => a.Status != AttendanceStatus.Absent), inMonth.Count)));
        }

        var top = completed.GroupBy(s => s.TeacherUserId).Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(5).ToList();
        var names = await db.People.NamesAsync(top.Select(t => t.Key), ct);

        int activeStudents;
        if (teacherIds is null)
        {
            activeStudents = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
        }
        else
        {
            // Sequential on purpose: one DbContext cannot run queries in parallel.
            var supervised = new HashSet<long>();
            foreach (var teacherId in teacherIds)
            {
                supervised.UnionWith(await guard.TeacherStudentIdsAsync(teacherId, ct));
            }

            activeStudents = supervised.Count;
        }

        return new AcademicStatsDto(
            activeStudents,
            teacherIds?.Count ?? await db.Teachers.CountAsync(ct),
            statusCounts.GetValueOrDefault(SessionStatus.Scheduled),
            statusCounts.GetValueOrDefault(SessionStatus.Completed),
            statusCounts.GetValueOrDefault(SessionStatus.Cancelled),
            Rate(attendance.Count(a => a.Status != AttendanceStatus.Absent), attendance.Count),
            monthly,
            top.Select(t => new TeacherSessionsDto(t.Key, names.GetValueOrDefault(t.Key, $"#{t.Key}"), t.Count)).ToList());
    }

    private async Task<StudentOverviewDto> StudentOverviewAsync(long studentId, DateTime now, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == studentId, ct);
        var name = (await db.People.NamesAsync([studentId], ct)).GetValueOrDefault(studentId, $"#{studentId}");
        var statuses = await db.Attendances.Where(a => a.StudentUserId == studentId)
            .GroupBy(a => a.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var upcoming = await sessions.ListAsync(new SessionQuery(now, now.AddDays(14), StudentUserId: studentId, Status: nameof(SessionStatus.Scheduled)), ct);
        var feedback = await sessions.StudentFeedbackAsync(studentId, 5, ct);
        var points = await db.Points.Where(p => p.StudentUserId == studentId).SumAsync(p => (int?)p.Points, ct) ?? 0;

        return new StudentOverviewDto(
            studentId, name, student?.Level,
            new AttendanceSummaryDto(
                statuses.GetValueOrDefault(AttendanceStatus.Present), statuses.GetValueOrDefault(AttendanceStatus.Late), statuses.GetValueOrDefault(AttendanceStatus.Absent)),
            upcoming.Take(5).ToList(), feedback, points);
    }

    private static double Rate(int attended, int total) => total == 0 ? 0 : Math.Round(attended * 100.0 / total, 1);
}

// ---------- Background work ----------

/// <summary>
/// Creates role profiles when Identity creates or changes a user (US-020). Runs as a system
/// user scoped to the user's academy.
/// </summary>
public interface IProfileSync
{
    Task SyncAsync(long academyId, long userId, IReadOnlyList<string> roles, CancellationToken ct = default);
}

internal sealed class ProfileSync(IAcademicDbContext db, TimeProvider clock) : IProfileSync
{
    public async Task SyncAsync(long academyId, long userId, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        bool Has(string role) => roles.Contains(role, StringComparer.OrdinalIgnoreCase);

        if (Has(Roles.Student) && !await db.Students.AnyAsync(s => s.UserId == userId, ct))
        {
            db.Students.Add(new Student { AcademyId = academyId, UserId = userId, EnrollmentDate = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) });
        }

        if (Has(Roles.Teacher) && !await db.Teachers.AnyAsync(t => t.UserId == userId, ct))
        {
            db.Teachers.Add(new Teacher { AcademyId = academyId, UserId = userId });
        }

        if (Has(Roles.Supervisor) && !await db.Supervisors.AnyAsync(s => s.UserId == userId, ct))
        {
            db.Supervisors.Add(new Supervisor { AcademyId = academyId, UserId = userId });
        }

        if (Has(Roles.Parent) && !await db.Parents.AnyAsync(p => p.UserId == userId, ct))
        {
            db.Parents.Add(new Parent { AcademyId = academyId, UserId = userId });
        }

        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Announces sessions starting within the next 24 hours to the teacher, the students and their
/// parents (US-035). Each session is announced once.
/// </summary>
public interface ISessionReminderService
{
    Task<int> SendDueRemindersAsync(CancellationToken ct = default);
}

internal sealed class SessionReminderService(IAcademicDbContext db, IEventPublisher events, TimeProvider clock) : ISessionReminderService
{
    public async Task<int> SendDueRemindersAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Sessions
            .Where(s => s.Status == SessionStatus.Scheduled && s.ReminderSentOnUtc == null && s.StartsAtUtc > now && s.StartsAtUtc <= now.AddHours(24))
            .Take(200)
            .ToListAsync(ct);

        foreach (var session in due)
        {
            var parent = await db.Students.Where(s => s.UserId == session.StudentUserId).Select(s => s.ParentUserId).FirstOrDefaultAsync(ct);
            var recipients = new[] { session.StudentUserId, session.TeacherUserId }
                .Concat(parent is { } p ? [p] : Array.Empty<long>()).Distinct().ToList();

            await events.PublishAsync(new SessionReminderDue(session.AcademyId, session.Id, session.Title, session.StartsAtUtc, recipients), ct);
            session.ReminderSentOnUtc = now;
        }

        await db.SaveChangesAsync(ct);
        return due.Count;
    }
}
