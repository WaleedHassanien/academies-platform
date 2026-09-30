using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

public sealed record PersonRefDto(long UserId, string FullName);

public sealed record SetMembersRequest(IReadOnlyList<long> UserIds);

public sealed record CourseDto(long Id, string Name, string? Description, string? Level, string Kind, bool IsActive, int MaterialCount);

public sealed record SaveCourseRequest(string Name, string? Description, string? Level, bool IsActive, CourseKind Kind = CourseKind.Other);

public sealed record MaterialDto(long Id, long CourseId, string Title, string Url, string Type, DateTime CreatedOnUtc);

public sealed record SaveMaterialRequest(string Title, string Url, MaterialType Type);

public sealed record EnrollmentDto(
    long Id, long StudentUserId, string? StudentName, long CourseId, string? CourseName, string CourseKind, long? TeacherUserId, string? TeacherName,
    string Status, DateOnly StartedOn, DateOnly? EndedOn);

/// <summary><see cref="StartedOn"/> defaults to today.</summary>
public sealed record SaveEnrollmentRequest(long CourseId, long? TeacherUserId, EnrollmentStatus Status = EnrollmentStatus.Active, DateOnly? StartedOn = null);

public sealed record SetTeacherCoursesRequest(IReadOnlyList<long> CourseIds);

// ---------- Relationships (US-022, US-023) ----------

public interface IRelationshipService
{
    Task<IReadOnlyList<PersonRefDto>> SupervisorTeachersAsync(long supervisorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<PersonRefDto>> SetSupervisorTeachersAsync(long supervisorUserId, IReadOnlyList<long> teacherUserIds, CancellationToken ct = default);
    Task<IReadOnlyList<PersonRefDto>> TeacherStudentsAsync(long teacherUserId, CancellationToken ct = default);
    Task<IReadOnlyList<PersonRefDto>> SetTeacherStudentsAsync(long teacherUserId, IReadOnlyList<long> studentUserIds, CancellationToken ct = default);

    /// <summary>The subjects a teacher may teach (empty: any).</summary>
    Task<IReadOnlyList<long>> TeacherCoursesAsync(long teacherUserId, CancellationToken ct = default);

    Task<IReadOnlyList<long>> SetTeacherCoursesAsync(long teacherUserId, IReadOnlyList<long> courseIds, CancellationToken ct = default);
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
        await db.EnsureStudentsAsync(studentUserIds, ct);

        var existing = await db.TeacherStudents.Where(t => t.TeacherUserId == teacherUserId).ToListAsync(ct);
        db.TeacherStudents.RemoveRange(existing.Where(e => !studentUserIds.Contains(e.StudentUserId)));
        foreach (var student in studentUserIds.Distinct().Where(s => existing.All(e => e.StudentUserId != s)))
        {
            db.TeacherStudents.Add(new TeacherStudent { TeacherUserId = teacherUserId, StudentUserId = student });
        }

        await db.SaveChangesAsync(ct);
        return await TeacherStudentsAsync(teacherUserId, ct);
    }

    public async Task<IReadOnlyList<long>> TeacherCoursesAsync(long teacherUserId, CancellationToken ct = default) =>
        await db.TeacherCourses.AsNoTracking().Where(t => t.TeacherUserId == teacherUserId).Select(t => t.CourseId).Distinct().ToListAsync(ct);

    public async Task<IReadOnlyList<long>> SetTeacherCoursesAsync(long teacherUserId, IReadOnlyList<long> courseIds, CancellationToken ct = default)
    {
        await db.People.EnsureRoleAsync(teacherUserId, Roles.Teacher, ct);
        var wanted = courseIds.Distinct().ToList();
        if (await db.Courses.CountAsync(c => wanted.Contains(c.Id), ct) != wanted.Count)
        {
            throw new BusinessRuleException("Some of the chosen subjects don't exist.");
        }

        var existing = await db.TeacherCourses.Where(t => t.TeacherUserId == teacherUserId).ToListAsync(ct);
        db.TeacherCourses.RemoveRange(existing.Where(e => !wanted.Contains(e.CourseId)));
        foreach (var course in wanted.Where(c => existing.All(e => e.CourseId != c)))
        {
            db.TeacherCourses.Add(new TeacherCourse { TeacherUserId = teacherUserId, CourseId = course });
        }

        await db.SaveChangesAsync(ct);
        return await TeacherCoursesAsync(teacherUserId, ct);
    }

    private async Task<IReadOnlyList<PersonRefDto>> RefsAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var names = await db.People.NamesAsync(ids, ct);
        return names.Select(n => new PersonRefDto(n.Key, n.Value)).OrderBy(n => n.FullName).ToList();
    }
}

// ---------- Enrollments: a student's subjects ----------

public interface IEnrollmentService
{
    Task<IReadOnlyList<EnrollmentDto>> ListAsync(long studentUserId, CancellationToken ct = default);
    Task<EnrollmentDto> AddAsync(long studentUserId, SaveEnrollmentRequest request, CancellationToken ct = default);
    Task<EnrollmentDto> UpdateAsync(long enrollmentId, SaveEnrollmentRequest request, CancellationToken ct = default);
}

internal sealed class EnrollmentService(IAcademicDbContext db, AccessGuard guard, TimeProvider clock) : IEnrollmentService
{
    public async Task<IReadOnlyList<EnrollmentDto>> ListAsync(long studentUserId, CancellationToken ct = default)
    {
        await guard.EnsureCanViewStudentAsync(studentUserId, ct);
        var rows = await db.Enrollments.AsNoTracking().Where(e => e.StudentUserId == studentUserId)
            .OrderBy(e => e.Status).ThenBy(e => e.Id).ToListAsync(ct);
        return await db.EnrollmentDtosAsync(rows, ct);
    }

    public async Task<EnrollmentDto> AddAsync(long studentUserId, SaveEnrollmentRequest request, CancellationToken ct = default)
    {
        await db.EnsureStudentsAsync([studentUserId], ct);
        if (await db.Enrollments.AnyAsync(e => e.StudentUserId == studentUserId && e.CourseId == request.CourseId && e.Status != EnrollmentStatus.Ended, ct))
        {
            throw new ConflictException("The student is already enrolled in this subject.");
        }

        var enrollment = new Enrollment
        {
            StudentUserId = studentUserId, CourseId = request.CourseId,
            StartedOn = request.StartedOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
        };
        await ApplyAsync(enrollment, request, ct);
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync(ct);
        return (await db.EnrollmentDtosAsync([enrollment], ct))[0];
    }

    public async Task<EnrollmentDto> UpdateAsync(long enrollmentId, SaveEnrollmentRequest request, CancellationToken ct = default)
    {
        var enrollment = await db.Enrollments.FirstOrDefaultAsync(e => e.Id == enrollmentId, ct) ?? throw new NotFoundException(nameof(Enrollment), enrollmentId);
        if (request.CourseId != enrollment.CourseId)
        {
            throw new BusinessRuleException("End this enrollment and add a new one to change the subject.");
        }

        await ApplyAsync(enrollment, request, ct);
        if (request.StartedOn is { } started)
        {
            enrollment.StartedOn = started;
        }

        await db.SaveChangesAsync(ct);
        return (await db.EnrollmentDtosAsync([enrollment], ct))[0];
    }

    private async Task ApplyAsync(Enrollment enrollment, SaveEnrollmentRequest request, CancellationToken ct)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == request.CourseId, ct))
        {
            throw new NotFoundException(nameof(Course), request.CourseId);
        }

        if (request.TeacherUserId is { } teacher)
        {
            await db.People.EnsureRoleAsync(teacher, Roles.Teacher, ct);
            await db.EnsureTeacherQualifiedAsync(teacher, request.CourseId, ct);
            await db.EnsureTeacherLinkAsync(teacher, enrollment.StudentUserId, ct);
        }

        enrollment.TeacherUserId = request.TeacherUserId;
        enrollment.Status = request.Status;
        enrollment.EndedOn = request.Status == EnrollmentStatus.Ended
            ? enrollment.EndedOn ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)
            : null;
    }
}

/// <summary>Enrollment and qualification rules shared by scheduling, enrollments and lead conversion.</summary>
internal static class EnrollmentRules
{
    public static async Task EnsureStudentsAsync(this IAcademicDbContext db, IReadOnlyList<long> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        var found = await db.Students.CountAsync(s => distinct.Contains(s.UserId), ct);
        if (found != distinct.Count)
        {
            throw new BusinessRuleException("Some of the selected users are not students of this academy.");
        }
    }

    /// <summary>A teacher with listed subjects may only teach those; one with none listed may teach any.</summary>
    public static async Task EnsureTeacherQualifiedAsync(this IAcademicDbContext db, long teacherUserId, long courseId, CancellationToken ct)
    {
        var subjects = await db.TeacherCourses.Where(t => t.TeacherUserId == teacherUserId).Select(t => t.CourseId).ToListAsync(ct);
        if (subjects.Count > 0 && !subjects.Contains(courseId))
        {
            throw new BusinessRuleException("This teacher doesn't teach this subject. Add it to their specialisations first.");
        }
    }

    /// <summary>Makes the teacher responsible for the student, if they weren't already.</summary>
    public static async Task EnsureTeacherLinkAsync(this IAcademicDbContext db, long teacherUserId, long studentUserId, CancellationToken ct)
    {
        var tracked = db.TeacherStudents.Local.Any(t => t.TeacherUserId == teacherUserId && t.StudentUserId == studentUserId && !t.IsDeleted);
        if (!tracked && !await db.TeacherStudents.AnyAsync(t => t.TeacherUserId == teacherUserId && t.StudentUserId == studentUserId, ct))
        {
            db.TeacherStudents.Add(new TeacherStudent { TeacherUserId = teacherUserId, StudentUserId = studentUserId });
        }
    }

    /// <summary>
    /// Scheduling a session enrols the student in its subject (with that teacher) if they weren't
    /// enrolled yet; an existing enrollment without a teacher gets this one.
    /// </summary>
    public static async Task EnsureEnrollmentAsync(this IAcademicDbContext db, long studentUserId, long courseId, long? teacherUserId, DateOnly today, CancellationToken ct)
    {
        var enrollment = db.Enrollments.Local.FirstOrDefault(e => e.StudentUserId == studentUserId && e.CourseId == courseId && e.Status != EnrollmentStatus.Ended)
                         ?? await db.Enrollments.FirstOrDefaultAsync(e => e.StudentUserId == studentUserId && e.CourseId == courseId && e.Status != EnrollmentStatus.Ended, ct);
        if (enrollment is null)
        {
            db.Enrollments.Add(new Enrollment { StudentUserId = studentUserId, CourseId = courseId, TeacherUserId = teacherUserId, StartedOn = today });
        }
        else
        {
            enrollment.TeacherUserId ??= teacherUserId;
        }
    }

    public static async Task<List<EnrollmentDto>> EnrollmentDtosAsync(this IAcademicDbContext db, IReadOnlyList<Enrollment> rows, CancellationToken ct)
    {
        var courseIds = rows.Select(r => r.CourseId).Distinct().ToList();
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var names = await db.People.NamesAsync(
            rows.Select(r => r.StudentUserId).Concat(rows.Where(r => r.TeacherUserId.HasValue).Select(r => r.TeacherUserId!.Value)), ct);
        return rows.Select(r => new EnrollmentDto(
            r.Id, r.StudentUserId, names.GetValueOrDefault(r.StudentUserId), r.CourseId, courses.GetValueOrDefault(r.CourseId)?.Name,
            (courses.GetValueOrDefault(r.CourseId)?.Kind ?? CourseKind.Other).ToString(), r.TeacherUserId,
            r.TeacherUserId is { } t ? names.GetValueOrDefault(t) : null, r.Status.ToString(), r.StartedOn, r.EndedOn)).ToList();
    }
}

// ---------- Subjects and materials (US-024, US-036) ----------

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
        (await db.Courses.AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new { c, Materials = db.Materials.Count(m => m.CourseId == c.Id) })
            .ToListAsync(ct))
        .Select(x => ToDto(x.c, x.Materials)).ToList();

    public async Task<CourseDto> GetAsync(long id, CancellationToken ct = default)
    {
        var course = await db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException(nameof(Course), id);
        return ToDto(course, await db.Materials.CountAsync(m => m.CourseId == id, ct));
    }

    public async Task<CourseDto> CreateAsync(SaveCourseRequest request, CancellationToken ct = default)
    {
        if (await db.Courses.AnyAsync(c => c.Name == request.Name.Trim(), ct))
        {
            throw new ConflictException($"A subject named '{request.Name}' already exists.");
        }

        var course = new Course
        {
            Name = request.Name.Trim(), Description = request.Description, Level = request.Level, IsActive = request.IsActive, Kind = request.Kind,
        };
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
        course.Kind = request.Kind;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var course = await db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException(nameof(Course), id);
        if (await db.Sessions.AnyAsync(s => s.CourseId == id, ct) || await db.Enrollments.AnyAsync(e => e.CourseId == id, ct))
        {
            throw new BusinessRuleException("The subject has sessions or students. Deactivate it instead of deleting.");
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

    private static CourseDto ToDto(Course c, int materials) =>
        new(c.Id, c.Name, c.Description, c.Level, c.Kind.ToString(), c.IsActive, materials);
}

internal sealed class SaveCourseValidator : AbstractValidator<SaveCourseRequest>
{
    public SaveCourseValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Level).MaximumLength(50);
        RuleFor(x => x.Kind).IsInEnum();
    }
}

internal sealed class SaveEnrollmentValidator : AbstractValidator<SaveEnrollmentRequest>
{
    public SaveEnrollmentValidator()
    {
        RuleFor(x => x.CourseId).GreaterThan(0);
        RuleFor(x => x.Status).IsInEnum();
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
