using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

public sealed record PersonRefDto(long UserId, string FullName);

public sealed record GroupDto(
    long Id, string Name, long? CourseId, string? CourseName, long? TeacherUserId, string? TeacherName, IReadOnlyList<PersonRefDto> Students);

public sealed record SaveGroupRequest(string Name, long? CourseId, long? TeacherUserId);

public sealed record SetMembersRequest(IReadOnlyList<long> UserIds);

public sealed record CourseDto(long Id, string Name, string? Description, string? Level, bool IsActive, int MaterialCount);

public sealed record SaveCourseRequest(string Name, string? Description, string? Level, bool IsActive);

public sealed record MaterialDto(long Id, long CourseId, string Title, string Url, string Type, DateTime CreatedOnUtc);

public sealed record SaveMaterialRequest(string Title, string Url, MaterialType Type);

// ---------- Relationships (US-022, US-023) ----------

public interface IRelationshipService
{
    Task<IReadOnlyList<PersonRefDto>> SupervisorTeachersAsync(long supervisorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<PersonRefDto>> SetSupervisorTeachersAsync(long supervisorUserId, IReadOnlyList<long> teacherUserIds, CancellationToken ct = default);
    Task<IReadOnlyList<PersonRefDto>> TeacherStudentsAsync(long teacherUserId, CancellationToken ct = default);
    Task<IReadOnlyList<PersonRefDto>> SetTeacherStudentsAsync(long teacherUserId, IReadOnlyList<long> studentUserIds, CancellationToken ct = default);
    Task<IReadOnlyList<GroupDto>> ListGroupsAsync(CancellationToken ct = default);
    Task<GroupDto> GetGroupAsync(long id, CancellationToken ct = default);
    Task<GroupDto> CreateGroupAsync(SaveGroupRequest request, CancellationToken ct = default);
    Task<GroupDto> UpdateGroupAsync(long id, SaveGroupRequest request, CancellationToken ct = default);
    Task DeleteGroupAsync(long id, CancellationToken ct = default);
    Task<GroupDto> SetGroupStudentsAsync(long id, IReadOnlyList<long> studentUserIds, CancellationToken ct = default);
}

internal sealed class RelationshipService(IAcademicDbContext db, AccessGuard guard) : IRelationshipService
{
    public async Task<IReadOnlyList<PersonRefDto>> SupervisorTeachersAsync(long supervisorUserId, CancellationToken ct = default)
    {
        if (!guard.IsStaff && guard.Me != supervisorUserId)
        {
            throw new ForbiddenAccessException();
        }

        return await RefsAsync(await guard.SupervisorTeacherIdsAsync(supervisorUserId, ct), ct);
    }

    /// <summary>Replaces the supervisor's teacher list: assign the new ones, unassign the rest.</summary>
    public async Task<IReadOnlyList<PersonRefDto>> SetSupervisorTeachersAsync(long supervisorUserId, IReadOnlyList<long> teacherUserIds, CancellationToken ct = default)
    {
        await db.People.EnsureRoleAsync(supervisorUserId, Roles.Supervisor, ct);
        foreach (var teacher in teacherUserIds.Distinct())
        {
            await db.People.EnsureRoleAsync(teacher, Roles.Teacher, ct);
        }

        var existing = await db.SupervisorTeachers.Where(s => s.SupervisorUserId == supervisorUserId).ToListAsync(ct);
        db.SupervisorTeachers.RemoveRange(existing.Where(e => !teacherUserIds.Contains(e.TeacherUserId)));
        foreach (var teacher in teacherUserIds.Distinct().Where(t => existing.All(e => e.TeacherUserId != t)))
        {
            db.SupervisorTeachers.Add(new SupervisorTeacher { SupervisorUserId = supervisorUserId, TeacherUserId = teacher });
        }

        await db.SaveChangesAsync(ct);
        return await SupervisorTeachersAsync(supervisorUserId, ct);
    }

    public async Task<IReadOnlyList<PersonRefDto>> TeacherStudentsAsync(long teacherUserId, CancellationToken ct = default)
    {
        var visibleTeachers = await guard.VisibleTeacherIdsAsync(ct);
        if (visibleTeachers is not null && !visibleTeachers.Contains(teacherUserId))
        {
            throw new ForbiddenAccessException();
        }

        return await RefsAsync(await guard.TeacherStudentIdsAsync(teacherUserId, ct), ct);
    }

    public async Task<IReadOnlyList<PersonRefDto>> SetTeacherStudentsAsync(long teacherUserId, IReadOnlyList<long> studentUserIds, CancellationToken ct = default)
    {
        await db.People.EnsureRoleAsync(teacherUserId, Roles.Teacher, ct);
        await EnsureStudentsAsync(studentUserIds, ct);

        var existing = await db.TeacherStudents.Where(t => t.TeacherUserId == teacherUserId).ToListAsync(ct);
        db.TeacherStudents.RemoveRange(existing.Where(e => !studentUserIds.Contains(e.StudentUserId)));
        foreach (var student in studentUserIds.Distinct().Where(s => existing.All(e => e.StudentUserId != s)))
        {
            db.TeacherStudents.Add(new TeacherStudent { TeacherUserId = teacherUserId, StudentUserId = student });
        }

        await db.SaveChangesAsync(ct);
        return await TeacherStudentsAsync(teacherUserId, ct);
    }

    public async Task<IReadOnlyList<GroupDto>> ListGroupsAsync(CancellationToken ct = default)
    {
        var q = db.Groups.AsNoTracking().Include(g => g.Students).AsQueryable();
        var visibleTeachers = await guard.VisibleTeacherIdsAsync(ct);
        if (visibleTeachers is not null)
        {
            var students = await guard.VisibleStudentIdsAsync(ct) ?? [];
            q = q.Where(g => (g.TeacherUserId != null && visibleTeachers.Contains(g.TeacherUserId.Value))
                             || g.Students.Any(s => students.Contains(s.StudentUserId)));
        }

        return await ToDtosAsync(await q.OrderBy(g => g.Name).ToListAsync(ct), ct);
    }

    public async Task<GroupDto> GetGroupAsync(long id, CancellationToken ct = default) =>
        (await ToDtosAsync([await LoadGroupAsync(id, ct)], ct))[0];

    public async Task<GroupDto> CreateGroupAsync(SaveGroupRequest request, CancellationToken ct = default)
    {
        await ValidateGroupAsync(request, ct);
        var group = new Group { Name = request.Name.Trim(), CourseId = request.CourseId, TeacherUserId = request.TeacherUserId };
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);
        return await GetGroupAsync(group.Id, ct);
    }

    public async Task<GroupDto> UpdateGroupAsync(long id, SaveGroupRequest request, CancellationToken ct = default)
    {
        await ValidateGroupAsync(request, ct);
        var group = await LoadGroupAsync(id, ct);
        group.Name = request.Name.Trim();
        group.CourseId = request.CourseId;
        group.TeacherUserId = request.TeacherUserId;
        await db.SaveChangesAsync(ct);
        return await GetGroupAsync(id, ct);
    }

    public async Task DeleteGroupAsync(long id, CancellationToken ct = default)
    {
        var group = await LoadGroupAsync(id, ct);
        if (await db.Sessions.AnyAsync(s => s.GroupId == id && s.Status == SessionStatus.Scheduled, ct))
        {
            throw new BusinessRuleException("The group has scheduled sessions. Cancel or reassign them first.");
        }

        db.GroupStudents.RemoveRange(group.Students);
        db.Groups.Remove(group);
        await db.SaveChangesAsync(ct);
    }

    public async Task<GroupDto> SetGroupStudentsAsync(long id, IReadOnlyList<long> studentUserIds, CancellationToken ct = default)
    {
        await EnsureStudentsAsync(studentUserIds, ct);
        var group = await LoadGroupAsync(id, ct);
        db.GroupStudents.RemoveRange(group.Students.Where(s => !studentUserIds.Contains(s.StudentUserId)));
        foreach (var student in studentUserIds.Distinct().Where(s => group.Students.All(gs => gs.StudentUserId != s)))
        {
            db.GroupStudents.Add(new GroupStudent { GroupId = id, StudentUserId = student });
        }

        await db.SaveChangesAsync(ct);
        return await GetGroupAsync(id, ct);
    }

    private async Task ValidateGroupAsync(SaveGroupRequest request, CancellationToken ct)
    {
        if (request.CourseId is { } courseId && !await db.Courses.AnyAsync(c => c.Id == courseId, ct))
        {
            throw new NotFoundException(nameof(Course), courseId);
        }

        if (request.TeacherUserId is { } teacherId)
        {
            await db.People.EnsureRoleAsync(teacherId, Roles.Teacher, ct);
        }
    }

    private async Task EnsureStudentsAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        var found = await db.Students.CountAsync(s => distinct.Contains(s.UserId), ct);
        if (found != distinct.Count)
        {
            throw new BusinessRuleException("Some of the selected users are not students of this academy.");
        }
    }

    private async Task<Group> LoadGroupAsync(long id, CancellationToken ct) =>
        await db.Groups.Include(g => g.Students).FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new NotFoundException(nameof(Group), id);

    private async Task<IReadOnlyList<PersonRefDto>> RefsAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var names = await db.People.NamesAsync(ids, ct);
        return names.Select(n => new PersonRefDto(n.Key, n.Value)).OrderBy(n => n.FullName).ToList();
    }

    private async Task<List<GroupDto>> ToDtosAsync(IReadOnlyList<Group> groups, CancellationToken ct)
    {
        var people = await db.People.NamesAsync(
            groups.SelectMany(g => g.Students.Select(s => s.StudentUserId)).Concat(groups.Where(g => g.TeacherUserId.HasValue).Select(g => g.TeacherUserId!.Value)), ct);
        var courseIds = groups.Where(g => g.CourseId.HasValue).Select(g => g.CourseId!.Value).Distinct().ToList();
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return groups.Select(g => new GroupDto(
            g.Id, g.Name, g.CourseId, g.CourseId is { } c ? courses.GetValueOrDefault(c) : null,
            g.TeacherUserId, g.TeacherUserId is { } t ? people.GetValueOrDefault(t) : null,
            g.Students.Select(s => new PersonRefDto(s.StudentUserId, people.GetValueOrDefault(s.StudentUserId, $"#{s.StudentUserId}"))).OrderBy(s => s.FullName).ToList()))
            .ToList();
    }
}

// ---------- Courses and materials (US-024, US-036) ----------

public interface ICourseService
{
    Task<IReadOnlyList<CourseDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<CourseDto> GetAsync(long id, CancellationToken ct = default);
    Task<CourseDto> CreateAsync(SaveCourseRequest request, CancellationToken ct = default);
    Task<CourseDto> UpdateAsync(long id, SaveCourseRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<MaterialDto>> MaterialsAsync(long courseId, CancellationToken ct = default);
    Task<MaterialDto> AddMaterialAsync(long courseId, SaveMaterialRequest request, CancellationToken ct = default);
    Task DeleteMaterialAsync(long courseId, long materialId, CancellationToken ct = default);
}

internal sealed class CourseService(IAcademicDbContext db) : ICourseService
{
    public async Task<IReadOnlyList<CourseDto>> ListAsync(bool includeInactive, CancellationToken ct = default) =>
        await db.Courses.AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CourseDto(c.Id, c.Name, c.Description, c.Level, c.IsActive, db.Materials.Count(m => m.CourseId == c.Id)))
            .ToListAsync(ct);

    public async Task<CourseDto> GetAsync(long id, CancellationToken ct = default) =>
        await db.Courses.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new CourseDto(c.Id, c.Name, c.Description, c.Level, c.IsActive, db.Materials.Count(m => m.CourseId == c.Id)))
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException(nameof(Course), id);

    public async Task<CourseDto> CreateAsync(SaveCourseRequest request, CancellationToken ct = default)
    {
        if (await db.Courses.AnyAsync(c => c.Name == request.Name.Trim(), ct))
        {
            throw new ConflictException($"A course named '{request.Name}' already exists.");
        }

        var course = new Course { Name = request.Name.Trim(), Description = request.Description, Level = request.Level, IsActive = request.IsActive };
        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        return await GetAsync(course.Id, ct);
    }

    public async Task<CourseDto> UpdateAsync(long id, SaveCourseRequest request, CancellationToken ct = default)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException(nameof(Course), id);
        course.Name = request.Name.Trim();
        course.Description = request.Description;
        course.Level = request.Level;
        course.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException(nameof(Course), id);
        if (await db.Sessions.AnyAsync(s => s.CourseId == id, ct))
        {
            throw new BusinessRuleException("The course has sessions. Deactivate it instead of deleting.");
        }

        db.Courses.Remove(course);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MaterialDto>> MaterialsAsync(long courseId, CancellationToken ct = default) =>
        await db.Materials.AsNoTracking().Where(m => m.CourseId == courseId).OrderByDescending(m => m.Id)
            .Select(m => new MaterialDto(m.Id, m.CourseId, m.Title, m.Url, m.Type.ToString(), m.CreatedOnUtc))
            .ToListAsync(ct);

    public async Task<MaterialDto> AddMaterialAsync(long courseId, SaveMaterialRequest request, CancellationToken ct = default)
    {
        _ = await GetAsync(courseId, ct);
        var material = new Material { CourseId = courseId, Title = request.Title.Trim(), Url = request.Url.Trim(), Type = request.Type };
        db.Materials.Add(material);
        await db.SaveChangesAsync(ct);
        return new MaterialDto(material.Id, courseId, material.Title, material.Url, material.Type.ToString(), material.CreatedOnUtc);
    }

    public async Task DeleteMaterialAsync(long courseId, long materialId, CancellationToken ct = default)
    {
        var material = await db.Materials.FirstOrDefaultAsync(m => m.Id == materialId && m.CourseId == courseId, ct)
            ?? throw new NotFoundException(nameof(Material), materialId);
        db.Materials.Remove(material);
        await db.SaveChangesAsync(ct);
    }
}

internal sealed class SaveGroupValidator : AbstractValidator<SaveGroupRequest>
{
    public SaveGroupValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

internal sealed class SaveCourseValidator : AbstractValidator<SaveCourseRequest>
{
    public SaveCourseValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Level).MaximumLength(50);
    }
}

internal sealed class SaveMaterialValidator : AbstractValidator<SaveMaterialRequest>
{
    public SaveMaterialValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Url).NotEmpty().MaximumLength(1000)
            .Must(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            .WithMessage("Enter a full http(s) link.");
    }
}
