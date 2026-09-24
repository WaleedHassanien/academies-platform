using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

// ---------- DTOs ----------

public sealed record StudentDto(
    long UserId, string FullName, string Email, string? Level, DateOnly EnrollmentDate, string Status,
    long? ParentUserId, string? ParentName, IReadOnlyList<string> Groups);

public sealed record StudentQuery(string? Search = null, long? GroupId = null, long? TeacherUserId = null, string? Status = null, int Page = 1, int PageSize = 20);

public sealed record UpdateStudentRequest(string? Level, DateOnly EnrollmentDate, StudentStatus Status);

public sealed record SetParentRequest(long? ParentUserId);

public sealed record StaffProfileDto(long UserId, string FullName, string Email, bool IsActive, string? Specialization, string? Notes, int LinkedCount);

public sealed record UpdateTeacherRequest(string? Specialization, string? Bio);

public sealed record UpdateSupervisorRequest(string? Notes);

public sealed record ParentDto(long UserId, string FullName, string Email, string? Occupation, IReadOnlyList<ChildDto> Children);

public sealed record ChildDto(long UserId, string FullName, string? Level, string Status);

public sealed record UpdateParentRequest(string? Occupation);

public sealed record WorkDayDto(DayOfWeek Day, bool IsWorkingDay, TimeOnly? ShiftStart, TimeOnly? ShiftEnd);

public sealed record SaveWorkScheduleRequest(IReadOnlyList<WorkDayDto> Days);

// ---------- Profiles (US-020) ----------

public interface IProfileService
{
    Task<PagedResult<StudentDto>> ListStudentsAsync(StudentQuery query, CancellationToken ct = default);
    Task<StudentDto> GetStudentAsync(long userId, CancellationToken ct = default);
    Task<StudentDto> UpdateStudentAsync(long userId, UpdateStudentRequest request, CancellationToken ct = default);
    Task<StudentDto> SetParentAsync(long studentUserId, long? parentUserId, CancellationToken ct = default);
    Task<IReadOnlyList<StaffProfileDto>> ListTeachersAsync(CancellationToken ct = default);
    Task<StaffProfileDto> UpdateTeacherAsync(long userId, UpdateTeacherRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<StaffProfileDto>> ListSupervisorsAsync(CancellationToken ct = default);
    Task<StaffProfileDto> UpdateSupervisorAsync(long userId, UpdateSupervisorRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ParentDto>> ListParentsAsync(CancellationToken ct = default);
    Task<ParentDto> GetParentAsync(long userId, CancellationToken ct = default);
    Task<ParentDto> UpdateParentAsync(long userId, UpdateParentRequest request, CancellationToken ct = default);
}

internal sealed class ProfileService(IAcademicDbContext db, AccessGuard guard, IEventPublisher events) : IProfileService
{
    public async Task<PagedResult<StudentDto>> ListStudentsAsync(StudentQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.Students.AsNoTracking().AsQueryable();

        var visible = await guard.VisibleStudentIdsAsync(ct);
        if (visible is not null)
        {
            q = q.Where(s => visible.Contains(s.UserId));
        }

        if (query.GroupId is { } groupId)
        {
            q = q.Where(s => db.GroupStudents.Any(gs => gs.GroupId == groupId && gs.StudentUserId == s.UserId));
        }

        if (query.TeacherUserId is { } teacherId)
        {
            var ids = await guard.TeacherStudentIdsAsync(teacherId, ct);
            q = q.Where(s => ids.Contains(s.UserId));
        }

        if (Enum.TryParse<StudentStatus>(query.Status, true, out var status))
        {
            q = q.Where(s => s.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            q = q.Where(s => db.People.Any(p => p.UserId == s.UserId && (p.FullName.Contains(query.Search) || p.Email.Contains(query.Search))));
        }

        var total = await q.CountAsync(ct);
        var students = await q.OrderBy(s => s.UserId).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<StudentDto>
        {
            Items = await ToDtosAsync(students, ct), Page = page.SafePage, PageSize = page.SafePageSize, TotalCount = total,
        };
    }

    public async Task<StudentDto> GetStudentAsync(long userId, CancellationToken ct = default)
    {
        await guard.EnsureCanViewStudentAsync(userId, ct);
        var student = await db.Students.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct)
            ?? throw new NotFoundException(nameof(Student), userId);
        return (await ToDtosAsync([student], ct))[0];
    }

    public async Task<StudentDto> UpdateStudentAsync(long userId, UpdateStudentRequest request, CancellationToken ct = default)
    {
        var student = await LoadStudentAsync(userId, ct);
        student.Level = request.Level;
        student.EnrollmentDate = request.EnrollmentDate;
        student.Status = request.Status;
        await db.SaveChangesAsync(ct);
        return await GetStudentAsync(userId, ct);
    }

    public async Task<StudentDto> SetParentAsync(long studentUserId, long? parentUserId, CancellationToken ct = default)
    {
        var student = await LoadStudentAsync(studentUserId, ct);
        if (parentUserId is { } parentId)
        {
            await db.People.EnsureRoleAsync(parentId, Roles.Parent, ct);
        }

        if (student.ParentUserId != parentUserId)
        {
            student.ParentUserId = parentUserId;
            await events.PublishAsync(new StudentParentChanged(student.AcademyId, studentUserId, parentUserId), ct);
            await db.SaveChangesAsync(ct);
        }

        return await GetStudentAsync(studentUserId, ct);
    }

    public async Task<IReadOnlyList<StaffProfileDto>> ListTeachersAsync(CancellationToken ct = default)
    {
        var visible = await guard.VisibleTeacherIdsAsync(ct);
        var teachers = await db.Teachers.AsNoTracking()
            .Where(t => visible == null || visible.Contains(t.UserId))
            .ToListAsync(ct);
        var ids = teachers.Select(t => t.UserId).ToList();
        var people = await db.People.Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        var result = new List<StaffProfileDto>();
        foreach (var t in teachers)
        {
            var students = (await guard.TeacherStudentIdsAsync(t.UserId, ct)).Count;
            result.Add(Staff(people, t.UserId, t.Specialization, t.Bio, students));
        }

        return result.OrderBy(r => r.FullName).ToList();
    }

    public async Task<StaffProfileDto> UpdateTeacherAsync(long userId, UpdateTeacherRequest request, CancellationToken ct = default)
    {
        var teacher = await db.Teachers.FirstOrDefaultAsync(t => t.UserId == userId, ct) ?? throw new NotFoundException(nameof(Teacher), userId);
        teacher.Specialization = request.Specialization;
        teacher.Bio = request.Bio;
        await db.SaveChangesAsync(ct);
        return (await ListTeachersAsync(ct)).First(t => t.UserId == userId);
    }

    public async Task<IReadOnlyList<StaffProfileDto>> ListSupervisorsAsync(CancellationToken ct = default)
    {
        var supervisors = await db.Supervisors.AsNoTracking().ToListAsync(ct);
        var ids = supervisors.Select(s => s.UserId).ToList();
        var people = await db.People.Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        var counts = await db.SupervisorTeachers.GroupBy(s => s.SupervisorUserId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        return supervisors
            .Select(s => Staff(people, s.UserId, null, s.Notes, counts.GetValueOrDefault(s.UserId)))
            .OrderBy(s => s.FullName)
            .ToList();
    }

    public async Task<StaffProfileDto> UpdateSupervisorAsync(long userId, UpdateSupervisorRequest request, CancellationToken ct = default)
    {
        var supervisor = await db.Supervisors.FirstOrDefaultAsync(s => s.UserId == userId, ct) ?? throw new NotFoundException(nameof(Supervisor), userId);
        supervisor.Notes = request.Notes;
        await db.SaveChangesAsync(ct);
        return (await ListSupervisorsAsync(ct)).First(s => s.UserId == userId);
    }

    public async Task<IReadOnlyList<ParentDto>> ListParentsAsync(CancellationToken ct = default)
    {
        var parents = await db.Parents.AsNoTracking().ToListAsync(ct);
        var result = new List<ParentDto>();
        foreach (var parent in parents)
        {
            result.Add(await ToParentDtoAsync(parent, ct));
        }

        return result.OrderBy(p => p.FullName).ToList();
    }

    public async Task<ParentDto> GetParentAsync(long userId, CancellationToken ct = default)
    {
        if (!guard.IsStaff && guard.Me != userId)
        {
            throw new ForbiddenAccessException();
        }

        var parent = await db.Parents.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? throw new NotFoundException(nameof(Parent), userId);
        return await ToParentDtoAsync(parent, ct);
    }

    public async Task<ParentDto> UpdateParentAsync(long userId, UpdateParentRequest request, CancellationToken ct = default)
    {
        var parent = await db.Parents.FirstOrDefaultAsync(p => p.UserId == userId, ct) ?? throw new NotFoundException(nameof(Parent), userId);
        parent.Occupation = request.Occupation;
        await db.SaveChangesAsync(ct);
        return await ToParentDtoAsync(parent, ct);
    }

    private async Task<ParentDto> ToParentDtoAsync(Parent parent, CancellationToken ct)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.UserId == parent.UserId, ct);
        var children = await db.Students.Where(s => s.ParentUserId == parent.UserId).ToListAsync(ct);
        var names = await db.People.NamesAsync(children.Select(c => c.UserId), ct);
        return new ParentDto(
            parent.UserId, person?.FullName ?? $"#{parent.UserId}", person?.Email ?? "", parent.Occupation,
            children.Select(c => new ChildDto(c.UserId, names.GetValueOrDefault(c.UserId, $"#{c.UserId}"), c.Level, c.Status.ToString())).ToList());
    }

    private async Task<Student> LoadStudentAsync(long userId, CancellationToken ct) =>
        await db.Students.FirstOrDefaultAsync(s => s.UserId == userId, ct) ?? throw new NotFoundException(nameof(Student), userId);

    private async Task<List<StudentDto>> ToDtosAsync(IReadOnlyList<Student> students, CancellationToken ct)
    {
        var ids = students.Select(s => s.UserId).Concat(students.Where(s => s.ParentUserId.HasValue).Select(s => s.ParentUserId!.Value)).ToList();
        var people = await db.People.Where(p => ids.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        var studentIds = students.Select(s => s.UserId).ToList();
        var groups = await db.GroupStudents
            .Where(gs => studentIds.Contains(gs.StudentUserId))
            .Join(db.Groups, gs => gs.GroupId, g => g.Id, (gs, g) => new { gs.StudentUserId, g.Name })
            .ToListAsync(ct);

        return students.Select(s => new StudentDto(
            s.UserId,
            people.GetValueOrDefault(s.UserId)?.FullName ?? $"#{s.UserId}",
            people.GetValueOrDefault(s.UserId)?.Email ?? "",
            s.Level, s.EnrollmentDate, s.Status.ToString(), s.ParentUserId,
            s.ParentUserId is { } p ? people.GetValueOrDefault(p)?.FullName : null,
            groups.Where(g => g.StudentUserId == s.UserId).Select(g => g.Name).ToList())).ToList();
    }

    private static StaffProfileDto Staff(Dictionary<long, BuildingBlocks.Domain.Person> people, long userId, string? specialization, string? notes, int linked)
    {
        var person = people.GetValueOrDefault(userId);
        return new StaffProfileDto(userId, person?.FullName ?? $"#{userId}", person?.Email ?? "", person?.IsActive ?? true, specialization, notes, linked);
    }
}

// ---------- Work schedule (US-021) ----------

public interface IWorkScheduleService
{
    Task<IReadOnlyList<WorkDayDto>> GetAsync(long supervisorUserId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkDayDto>> SaveAsync(long supervisorUserId, SaveWorkScheduleRequest request, CancellationToken ct = default);
}

internal sealed class WorkScheduleService(IAcademicDbContext db) : IWorkScheduleService
{
    /// <summary>Saturday first, as the academy week starts on Saturday.</summary>
    private static readonly DayOfWeek[] Week =
        [DayOfWeek.Saturday, DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public async Task<IReadOnlyList<WorkDayDto>> GetAsync(long supervisorUserId, CancellationToken ct = default)
    {
        var rows = await db.WorkSchedules.AsNoTracking().Where(w => w.SupervisorUserId == supervisorUserId).ToListAsync(ct);
        return Week.Select(day => rows.FirstOrDefault(r => r.Day == day) is { } r
                ? new WorkDayDto(day, r.IsWorkingDay, r.ShiftStart, r.ShiftEnd)
                : new WorkDayDto(day, false, null, null))
            .ToList();
    }

    public async Task<IReadOnlyList<WorkDayDto>> SaveAsync(long supervisorUserId, SaveWorkScheduleRequest request, CancellationToken ct = default)
    {
        await db.People.EnsureRoleAsync(supervisorUserId, Roles.Supervisor, ct);
        var rows = await db.WorkSchedules.Where(w => w.SupervisorUserId == supervisorUserId).ToListAsync(ct);

        foreach (var day in request.Days)
        {
            var row = rows.FirstOrDefault(r => r.Day == day.Day);
            if (row is null)
            {
                row = new WorkSchedule { SupervisorUserId = supervisorUserId, Day = day.Day };
                db.WorkSchedules.Add(row);
            }

            row.IsWorkingDay = day.IsWorkingDay;
            row.ShiftStart = day.IsWorkingDay ? day.ShiftStart : null;
            row.ShiftEnd = day.IsWorkingDay ? day.ShiftEnd : null;
        }

        await db.SaveChangesAsync(ct);
        return await GetAsync(supervisorUserId, ct);
    }
}

internal sealed class SaveWorkScheduleValidator : AbstractValidator<SaveWorkScheduleRequest>
{
    public SaveWorkScheduleValidator()
    {
        RuleFor(x => x.Days).NotEmpty()
            .Must(d => d.Select(x => x.Day).Distinct().Count() == d.Count).WithMessage("Each day may appear only once.")
            .Must(d => d.Any(x => x.IsWorkingDay)).WithMessage("At least one day must be a working day.");
        RuleForEach(x => x.Days).ChildRules(day =>
        {
            day.When(d => d.IsWorkingDay, () =>
            {
                day.RuleFor(d => d.ShiftStart).NotNull().WithMessage("Working days need a shift start.");
                day.RuleFor(d => d.ShiftEnd).NotNull().WithMessage("Working days need a shift end.");
                day.RuleFor(d => d).Must(d => d.ShiftEnd > d.ShiftStart)
                    .WithMessage("Shift end must be after shift start.")
                    .When(d => d.ShiftStart.HasValue && d.ShiftEnd.HasValue);
            });
        });
    }
}

internal sealed class UpdateStudentValidator : AbstractValidator<UpdateStudentRequest>
{
    public UpdateStudentValidator()
    {
        RuleFor(x => x.Level).MaximumLength(50);
        RuleFor(x => x.Status).IsInEnum();
    }
}
