using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Domain;
using Academies.Contracts.Security;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

public interface IAcademicDbContext
{
    DbSet<Person> People { get; }
    DbSet<Student> Students { get; }
    DbSet<Teacher> Teachers { get; }
    DbSet<Supervisor> Supervisors { get; }
    DbSet<Parent> Parents { get; }
    DbSet<WorkSchedule> WorkSchedules { get; }
    DbSet<SupervisorTeacher> SupervisorTeachers { get; }
    DbSet<TeacherStudent> TeacherStudents { get; }
    DbSet<Group> Groups { get; }
    DbSet<GroupStudent> GroupStudents { get; }
    DbSet<Course> Courses { get; }
    DbSet<Material> Materials { get; }
    DbSet<Session> Sessions { get; }
    DbSet<Attendance> Attendances { get; }
    DbSet<SessionFeedback> Feedbacks { get; }
    DbSet<SessionExcuse> Excuses { get; }
    DbSet<Assignment> Assignments { get; }
    DbSet<AssignmentSubmission> Submissions { get; }
    DbSet<Certificate> Certificates { get; }
    DbSet<PointEntry> Points { get; }
    DbSet<StudentBadge> Badges { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Who is joining an online session: the teacher (and staff) join as moderator.</summary>
public sealed record MeetingParticipant(long UserId, string Name, string Email, bool IsModerator);

/// <summary>
/// Online-session rooms (US-037). Every teacher has one permanent room of their own, so two
/// teachers never share a room even at the same hour; a teacher can't hold two sessions at once.
/// </summary>
public interface IMeetingLinkGenerator
{
    /// <summary>The teacher's room link, stored on their online sessions (no personal token in it).</summary>
    string RoomUrl(long academyId, long teacherUserId);

    /// <summary>Whether a session's link is one of our rooms (rather than an external link typed in by hand).</summary>
    bool IsOurRoom(string meetingUrl);

    /// <summary>
    /// A personal link into the teacher's room. With a Jitsi token provider (JaaS or a self-hosted
    /// server) it carries a signed token with the person's name and email, so nobody logs in to Jitsi
    /// and the teacher opens their room as moderator. The token only works between the two times.
    /// </summary>
    string JoinUrl(long academyId, long teacherUserId, MeetingParticipant participant, DateTime notBeforeUtc, DateTime expiresAtUtc);
}

public sealed record CertificateDocument(string StudentName, string CourseName, string Number, DateTime IssuedOnUtc, string AcademyName);

public interface ICertificateRenderer
{
    byte[] Render(CertificateDocument certificate);
}

/// <summary>
/// Relationship-based visibility shared by the academic services. Staff (Admin, Manager,
/// SuperAdmin) see everything in the academy. Everyone else sees only what their links allow:
/// <list type="bullet">
/// <item>a student sees themself;</item>
/// <item>a parent sees their children;</item>
/// <item>a teacher sees their own students;</item>
/// <item>a supervisor sees their teachers' students.</item>
/// </list>
/// </summary>
public sealed class AccessGuard(IAcademicDbContext db, ICurrentUser user)
{
    public bool IsStaff => user.IsSuperAdmin || user.IsInRole(Roles.Admin) || user.IsInRole(Roles.Manager);

    public long Me => user.UserId ?? throw new UnauthorizedException("Authentication is required.");

    public long AcademyId => user.AcademyId ?? throw new ForbiddenAccessException("This action needs an academy account.");

    public bool IsInRole(string role) => user.IsInRole(role);

    public bool HasPermission(string permission) => user.HasPermission(permission);

    public async Task<List<long>> TeacherStudentIdsAsync(long teacherUserId, CancellationToken ct = default)
    {
        var direct = db.TeacherStudents.Where(t => t.TeacherUserId == teacherUserId).Select(t => t.StudentUserId);
        var viaGroups = db.GroupStudents
            .Where(gs => db.Groups.Any(g => g.Id == gs.GroupId && g.TeacherUserId == teacherUserId))
            .Select(gs => gs.StudentUserId);
        return await direct.Union(viaGroups).ToListAsync(ct);
    }

    public Task<List<long>> SupervisorTeacherIdsAsync(long supervisorUserId, CancellationToken ct = default) =>
        db.SupervisorTeachers.Where(s => s.SupervisorUserId == supervisorUserId).Select(s => s.TeacherUserId).ToListAsync(ct);

    public Task<List<long>> ChildrenIdsAsync(long parentUserId, CancellationToken ct = default) =>
        db.Students.Where(s => s.ParentUserId == parentUserId).Select(s => s.UserId).ToListAsync(ct);

    /// <summary>
    /// The students this caller may see, or null for "all in the academy" (staff and
    /// accountants, who need names for billing).
    /// </summary>
    public async Task<HashSet<long>?> VisibleStudentIdsAsync(CancellationToken ct = default)
    {
        if (IsStaff || user.IsInRole(Roles.Accountant))
        {
            return null;
        }

        var ids = new HashSet<long>();
        if (user.IsInRole(Roles.Student))
        {
            ids.Add(Me);
        }

        if (user.IsInRole(Roles.Parent))
        {
            ids.UnionWith(await ChildrenIdsAsync(Me, ct));
        }

        if (user.IsInRole(Roles.Teacher))
        {
            ids.UnionWith(await TeacherStudentIdsAsync(Me, ct));
        }

        if (user.IsInRole(Roles.Supervisor))
        {
            foreach (var teacher in await SupervisorTeacherIdsAsync(Me, ct))
            {
                ids.UnionWith(await TeacherStudentIdsAsync(teacher, ct));
            }
        }

        return ids;
    }

    /// <summary>Teachers whose sessions this caller may see, or null for all.</summary>
    public async Task<HashSet<long>?> VisibleTeacherIdsAsync(CancellationToken ct = default)
    {
        if (IsStaff || user.IsInRole(Roles.Accountant))
        {
            return null;
        }

        var ids = new HashSet<long>();
        if (user.IsInRole(Roles.Teacher))
        {
            ids.Add(Me);
        }

        if (user.IsInRole(Roles.Supervisor))
        {
            ids.UnionWith(await SupervisorTeacherIdsAsync(Me, ct));
        }

        return ids;
    }

    public async Task EnsureCanViewStudentAsync(long studentUserId, CancellationToken ct = default)
    {
        var visible = await VisibleStudentIdsAsync(ct);
        if (visible is not null && !visible.Contains(studentUserId))
        {
            throw new ForbiddenAccessException("You can only see your own students or children.");
        }
    }

    /// <summary>Staff with the manage permission, or the session's own teacher.</summary>
    public void EnsureCanRunSession(Session session)
    {
        if (!(IsStaff || user.HasPermission(Permissions.Sessions.Manage)) && session.TeacherUserId != Me)
        {
            throw new ForbiddenAccessException("Only the session's teacher or academy staff can do this.");
        }
    }
}

internal static class PeopleLookups
{
    public static async Task<Dictionary<long, string>> NamesAsync(this DbSet<Person> people, IEnumerable<long> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        return ids.Count == 0 ? [] : await people.Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, p => p.FullName, ct);
    }

    public static async Task EnsureRoleAsync(this DbSet<Person> people, long userId, string role, CancellationToken ct)
    {
        var person = await people.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (person is null || !person.IsActive || !person.HasRole(role))
        {
            throw new BusinessRuleException($"User {userId} is not an active {role} in this academy.");
        }
    }
}
