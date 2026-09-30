using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

// Each student has a plan per subject, written by the teacher: there is no fixed curriculum, so the
// goal and the reference are free text agreed with the family (or the adult student).

public sealed record LearningPlanDto(
    long Id, long StudentUserId, long CourseId, string? CourseName, string? CourseKind, long TeacherUserId, string? TeacherName,
    string Goal, string? Reference, string? ExpectedAmount, DateOnly? TargetDate, string Status, string? Notes,
    DateTime CreatedOnUtc, DateTime? UpdatedOnUtc);

public sealed record SaveLearningPlanRequest(
    long CourseId, string Goal, string? Reference, string? ExpectedAmount, DateOnly? TargetDate,
    LearningPlanStatus Status = LearningPlanStatus.Active, string? Notes = null);

public interface ILearningPlanService
{
    Task<IReadOnlyList<LearningPlanDto>> ListAsync(long studentUserId, CancellationToken ct = default);
    Task<LearningPlanDto> CreateAsync(long studentUserId, SaveLearningPlanRequest request, CancellationToken ct = default);
    Task<LearningPlanDto> UpdateAsync(long planId, SaveLearningPlanRequest request, CancellationToken ct = default);
    Task DeleteAsync(long planId, CancellationToken ct = default);
}

/// <summary>
/// Anyone who may see the student reads their plans. Staff, and the student's own teachers, write
/// them. A new active plan for a subject closes the previous active one, so each subject has one.
/// </summary>
internal sealed class LearningPlanService(IAcademicDbContext db, AccessGuard guard) : ILearningPlanService
{
    public async Task<IReadOnlyList<LearningPlanDto>> ListAsync(long studentUserId, CancellationToken ct = default)
    {
        await guard.EnsureCanViewStudentAsync(studentUserId, ct);
        var plans = await db.LearningPlans.AsNoTracking().Where(p => p.StudentUserId == studentUserId)
            .OrderBy(p => p.Status).ThenByDescending(p => p.Id).ToListAsync(ct);
        return await ToDtosAsync(plans, ct);
    }

    public async Task<LearningPlanDto> CreateAsync(long studentUserId, SaveLearningPlanRequest request, CancellationToken ct = default)
    {
        await EnsureCanWriteAsync(studentUserId, ct);
        await EnsureCourseAsync(request.CourseId, ct);

        var plan = new LearningPlan
        {
            StudentUserId = studentUserId, CourseId = request.CourseId, Goal = request.Goal.Trim(),
            TeacherUserId = await AuthorAsync(studentUserId, request.CourseId, ct),
        };
        Apply(plan, request);
        await CloseOtherActiveAsync(plan, ct);
        db.LearningPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([plan], ct))[0];
    }

    public async Task<LearningPlanDto> UpdateAsync(long planId, SaveLearningPlanRequest request, CancellationToken ct = default)
    {
        var plan = await db.LearningPlans.FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new NotFoundException(nameof(LearningPlan), planId);
        await EnsureCanWriteAsync(plan.StudentUserId, ct);
        if (request.CourseId != plan.CourseId)
        {
            throw new BusinessRuleException("A plan belongs to one subject; write a new plan for another subject.");
        }

        Apply(plan, request);
        await CloseOtherActiveAsync(plan, ct);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([plan], ct))[0];
    }

    public async Task DeleteAsync(long planId, CancellationToken ct = default)
    {
        var plan = await db.LearningPlans.FirstOrDefaultAsync(p => p.Id == planId, ct) ?? throw new NotFoundException(nameof(LearningPlan), planId);
        await EnsureCanWriteAsync(plan.StudentUserId, ct);
        db.LearningPlans.Remove(plan);
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(LearningPlan plan, SaveLearningPlanRequest request)
    {
        plan.Goal = request.Goal.Trim();
        plan.Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        plan.ExpectedAmount = string.IsNullOrWhiteSpace(request.ExpectedAmount) ? null : request.ExpectedAmount.Trim();
        plan.TargetDate = request.TargetDate;
        plan.Status = request.Status;
        plan.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
    }

    private async Task CloseOtherActiveAsync(LearningPlan plan, CancellationToken ct)
    {
        if (plan.Status != LearningPlanStatus.Active)
        {
            return;
        }

        var others = await db.LearningPlans
            .Where(p => p.StudentUserId == plan.StudentUserId && p.CourseId == plan.CourseId && p.Status == LearningPlanStatus.Active && p.Id != plan.Id)
            .ToListAsync(ct);
        others.ForEach(p => p.Status = LearningPlanStatus.Closed);
    }

    /// <summary>The teacher behind the plan: the caller if a teacher, else the subject's teacher, else the caller.</summary>
    private async Task<long> AuthorAsync(long studentUserId, long courseId, CancellationToken ct)
    {
        if (guard.IsInRole(Roles.Teacher))
        {
            return guard.Me;
        }

        return await db.Enrollments
                   .Where(e => e.StudentUserId == studentUserId && e.CourseId == courseId && e.TeacherUserId != null && e.Status != EnrollmentStatus.Ended)
                   .Select(e => e.TeacherUserId).FirstOrDefaultAsync(ct)
               ?? guard.Me;
    }

    private async Task EnsureCanWriteAsync(long studentUserId, CancellationToken ct)
    {
        if (guard.IsStaff || guard.HasPermission(Permissions.Profiles.Manage))
        {
            return;
        }

        if (guard.IsInRole(Roles.Teacher) && (await guard.TeacherStudentIdsAsync(guard.Me, ct)).Contains(studentUserId))
        {
            return;
        }

        throw new ForbiddenAccessException("Only the student's teacher or academy staff can write their plan.");
    }

    private async Task EnsureCourseAsync(long courseId, CancellationToken ct)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == courseId, ct))
        {
            throw new NotFoundException(nameof(Course), courseId);
        }
    }

    private async Task<List<LearningPlanDto>> ToDtosAsync(IReadOnlyList<LearningPlan> plans, CancellationToken ct)
    {
        var courseIds = plans.Select(p => p.CourseId).Distinct().ToList();
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var names = await db.People.NamesAsync(plans.Select(p => p.TeacherUserId), ct);
        return plans.Select(p => new LearningPlanDto(
            p.Id, p.StudentUserId, p.CourseId, courses.GetValueOrDefault(p.CourseId)?.Name, courses.GetValueOrDefault(p.CourseId)?.Kind.ToString(),
            p.TeacherUserId, names.GetValueOrDefault(p.TeacherUserId), p.Goal, p.Reference, p.ExpectedAmount, p.TargetDate, p.Status.ToString(), p.Notes,
            p.CreatedOnUtc, p.UpdatedOnUtc)).ToList();
    }
}

internal sealed class SaveLearningPlanValidator : AbstractValidator<SaveLearningPlanRequest>
{
    public SaveLearningPlanValidator()
    {
        RuleFor(x => x.CourseId).GreaterThan(0);
        RuleFor(x => x.Goal).NotEmpty().MaximumLength(500);
        RuleFor(x => x.Reference).MaximumLength(500);
        RuleFor(x => x.ExpectedAmount).MaximumLength(200);
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
