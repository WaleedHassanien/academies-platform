using System.Security.Cryptography;
using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

// ---------- Gamification (US-042) ----------

public sealed record BadgeDto(string Code, string Name, int Threshold, DateTime? AwardedOnUtc);

public sealed record PointEntryDto(int Points, string Reason, DateTime CreatedOnUtc);

public sealed record StudentPointsDto(long StudentUserId, string? FullName, int Total, IReadOnlyList<BadgeDto> Badges, IReadOnlyList<PointEntryDto> Recent);

public sealed record LeaderboardEntryDto(int Rank, long StudentUserId, string FullName, int Total);

public interface IGamificationService
{
    /// <summary>
    /// Sets the award for one source event (e.g. one attendance row). Null points remove it.
    /// Updates badges. Does nothing when the plan lacks gamification. The caller saves.
    /// </summary>
    Task SetAwardAsync(long studentUserId, string sourceType, long sourceId, int? points, string reason, CancellationToken ct = default);

    Task<StudentPointsDto> GetAsync(long studentUserId, CancellationToken ct = default);
    Task<IReadOnlyList<LeaderboardEntryDto>> LeaderboardAsync(int top, CancellationToken ct = default);
}

internal sealed class GamificationService(
    IAcademicDbContext db, AccessGuard guard, IEntitlementsProvider entitlements, TimeProvider clock) : IGamificationService
{
    public async Task SetAwardAsync(long studentUserId, string sourceType, long sourceId, int? points, string reason, CancellationToken ct = default)
    {
        var academyId = guard.AcademyId;
        if (!(await entitlements.GetAsync(academyId, ct)).HasFeature(FeatureKeys.Gamification))
        {
            return;
        }

        var entry = await db.Points.FirstOrDefaultAsync(p => p.SourceType == sourceType && p.SourceId == sourceId && p.StudentUserId == studentUserId, ct);
        if (points is null)
        {
            if (entry is not null)
            {
                db.Points.Remove(entry);
            }
        }
        else if (entry is null)
        {
            db.Points.Add(new PointEntry { StudentUserId = studentUserId, Points = points.Value, Reason = reason, SourceType = sourceType, SourceId = sourceId });
        }
        else
        {
            entry.Points = points.Value;
            entry.Reason = reason;
        }

        await db.SaveChangesAsync(ct);
        await AwardBadgesAsync(studentUserId, ct);
    }

    public async Task<StudentPointsDto> GetAsync(long studentUserId, CancellationToken ct = default)
    {
        await guard.EnsureCanViewStudentAsync(studentUserId, ct);
        var total = await db.Points.Where(p => p.StudentUserId == studentUserId).SumAsync(p => (int?)p.Points, ct) ?? 0;
        var awarded = await db.Badges.Where(b => b.StudentUserId == studentUserId).ToDictionaryAsync(b => b.BadgeCode, b => b.AwardedOnUtc, ct);
        var recent = await db.Points.Where(p => p.StudentUserId == studentUserId).OrderByDescending(p => p.Id).Take(20)
            .Select(p => new PointEntryDto(p.Points, p.Reason, p.CreatedOnUtc)).ToListAsync(ct);
        var name = (await db.People.NamesAsync([studentUserId], ct)).GetValueOrDefault(studentUserId);

        var badges = GamificationRules.Badges
            .Select(b => new BadgeDto(b.Code, b.Name, b.Threshold, awarded.TryGetValue(b.Code, out var on) ? on : null))
            .ToList();
        return new StudentPointsDto(studentUserId, name, total, badges, recent);
    }

    public async Task<IReadOnlyList<LeaderboardEntryDto>> LeaderboardAsync(int top, CancellationToken ct = default)
    {
        var rows = await db.Points.GroupBy(p => p.StudentUserId)
            .Select(g => new { StudentUserId = g.Key, Total = g.Sum(p => p.Points) })
            .OrderByDescending(x => x.Total)
            .Take(Math.Clamp(top, 1, 100))
            .ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.Select(r => r.StudentUserId), ct);
        return rows.Select((r, i) => new LeaderboardEntryDto(i + 1, r.StudentUserId, names.GetValueOrDefault(r.StudentUserId, $"#{r.StudentUserId}"), r.Total)).ToList();
    }

    private async Task AwardBadgesAsync(long studentUserId, CancellationToken ct)
    {
        var total = await db.Points.Where(p => p.StudentUserId == studentUserId).SumAsync(p => (int?)p.Points, ct) ?? 0;
        var owned = await db.Badges.Where(b => b.StudentUserId == studentUserId).Select(b => b.BadgeCode).ToListAsync(ct);
        foreach (var badge in GamificationRules.Badges.Where(b => total >= b.Threshold && !owned.Contains(b.Code)))
        {
            db.Badges.Add(new StudentBadge { StudentUserId = studentUserId, BadgeCode = badge.Code, AwardedOnUtc = clock.GetUtcNow().UtcDateTime });
        }
    }
}

// ---------- Assignments (US-036) ----------

/// <summary><see cref="StudentUserId"/> null: for every student enrolled in the subject.</summary>
public sealed record AssignmentDto(
    long Id, long CourseId, string? CourseName, long? StudentUserId, string? StudentName, long TeacherUserId, string? TeacherName,
    string Title, string? Description, DateTime DueAtUtc, decimal MaxScore, int SubmissionCount, SubmissionDto? MySubmission);

public sealed record SubmissionDto(
    long Id, long AssignmentId, long StudentUserId, string? StudentName, string? Content, string? AttachmentUrl,
    DateTime SubmittedAtUtc, bool IsLate, decimal? Score, string? TeacherFeedback, DateTime? GradedAtUtc);

public sealed record SaveAssignmentRequest(long CourseId, long? StudentUserId, long? TeacherUserId, string Title, string? Description, DateTime DueAtUtc, decimal MaxScore);

public sealed record SubmitRequest(string? Content, string? AttachmentUrl);

public sealed record GradeRequest(decimal Score, string? Feedback);

public interface IAssignmentService
{
    Task<IReadOnlyList<AssignmentDto>> ListAsync(long? courseId, long? studentUserId, CancellationToken ct = default);
    Task<AssignmentDto> CreateAsync(SaveAssignmentRequest request, CancellationToken ct = default);
    Task<AssignmentDto> UpdateAsync(long id, SaveAssignmentRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<SubmissionDto>> SubmissionsAsync(long id, CancellationToken ct = default);
    Task<SubmissionDto> SubmitAsync(long id, SubmitRequest request, CancellationToken ct = default);
    Task<SubmissionDto> GradeAsync(long submissionId, GradeRequest request, CancellationToken ct = default);
}

internal sealed class AssignmentService(
    IAcademicDbContext db, AccessGuard guard, IGamificationService gamification, TimeProvider clock) : IAssignmentService
{
    /// <summary>
    /// Students see assignments set for them, plus subject-wide ones for subjects they are enrolled
    /// in. Teachers see their own (and supervisors their teachers'). Staff see all.
    /// </summary>
    public async Task<IReadOnlyList<AssignmentDto>> ListAsync(long? courseId, long? studentUserId, CancellationToken ct = default)
    {
        var q = db.Assignments.AsNoTracking().AsQueryable();
        if (courseId is { } c)
        {
            q = q.Where(a => a.CourseId == c);
        }

        if (studentUserId is { } sid)
        {
            q = q.Where(a => a.StudentUserId == sid || a.StudentUserId == null);
        }

        var isStudent = !guard.IsStaff && guard.IsInRole(Roles.Student);
        if (!guard.IsStaff)
        {
            var me = guard.Me;
            var myCourses = await db.Enrollments.Where(e => e.StudentUserId == me && e.Status != EnrollmentStatus.Ended)
                .Select(e => e.CourseId).ToListAsync(ct);
            var teachers = await guard.VisibleTeacherIdsAsync(ct) ?? [];
            q = q.Where(a => teachers.Contains(a.TeacherUserId)
                             || a.StudentUserId == me
                             || (a.StudentUserId == null && myCourses.Contains(a.CourseId)));
        }

        var list = await q.OrderByDescending(a => a.DueAtUtc).Take(500).ToListAsync(ct);
        return await ToDtosAsync(list, isStudent ? guard.Me : null, ct);
    }

    public async Task<AssignmentDto> CreateAsync(SaveAssignmentRequest request, CancellationToken ct = default)
    {
        var teacherId = ResolveTeacher(request.TeacherUserId);
        await ValidateAsync(request, teacherId, ct);
        var assignment = new Assignment
        {
            CourseId = request.CourseId, StudentUserId = request.StudentUserId, TeacherUserId = teacherId, Title = request.Title.Trim(),
            Description = request.Description, DueAtUtc = request.DueAtUtc, MaxScore = request.MaxScore,
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([assignment], null, ct))[0];
    }

    public async Task<AssignmentDto> UpdateAsync(long id, SaveAssignmentRequest request, CancellationToken ct = default)
    {
        var assignment = await LoadOwnedAsync(id, ct);
        var teacherId = ResolveTeacher(request.TeacherUserId ?? assignment.TeacherUserId);
        await ValidateAsync(request, teacherId, ct);
        assignment.CourseId = request.CourseId;
        assignment.StudentUserId = request.StudentUserId;
        assignment.TeacherUserId = teacherId;
        assignment.Title = request.Title.Trim();
        assignment.Description = request.Description;
        assignment.DueAtUtc = request.DueAtUtc;
        assignment.MaxScore = request.MaxScore;
        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([assignment], null, ct))[0];
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var assignment = await LoadOwnedAsync(id, ct);
        db.Assignments.Remove(assignment);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SubmissionDto>> SubmissionsAsync(long id, CancellationToken ct = default)
    {
        var assignment = await LoadOwnedAsync(id, ct);
        var rows = await db.Submissions.AsNoTracking().Where(s => s.AssignmentId == id).OrderBy(s => s.SubmittedAtUtc).ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.Select(r => r.StudentUserId), ct);
        return rows.Select(r => ToDto(r, assignment, names.GetValueOrDefault(r.StudentUserId))).ToList();
    }

    /// <summary>A student submits, or resubmits until graded. On-time submissions earn points.</summary>
    public async Task<SubmissionDto> SubmitAsync(long id, SubmitRequest request, CancellationToken ct = default)
    {
        if (!guard.IsInRole(Roles.Student))
        {
            throw new ForbiddenAccessException("Only students submit assignments.");
        }

        var me = guard.Me;
        var assignment = (await ListAsync(null, null, ct)).FirstOrDefault(a => a.Id == id)
            ?? throw new NotFoundException(nameof(Assignment), id);
        var entity = await db.Assignments.FirstAsync(a => a.Id == assignment.Id, ct);

        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.AssignmentId == id && s.StudentUserId == me, ct);
        if (submission?.GradedAtUtc is not null)
        {
            throw new BusinessRuleException("This submission has already been graded.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (submission is null)
        {
            submission = new AssignmentSubmission { AssignmentId = id, StudentUserId = me };
            db.Submissions.Add(submission);
        }

        submission.Content = request.Content;
        submission.AttachmentUrl = request.AttachmentUrl;
        submission.SubmittedAtUtc = now;
        await db.SaveChangesAsync(ct);

        await gamification.SetAwardAsync(
            me, "submission", submission.Id, now <= entity.DueAtUtc ? GamificationRules.OnTimeSubmission : null, $"On-time: {entity.Title}", ct);
        await db.SaveChangesAsync(ct);
        return ToDto(submission, entity, null);
    }

    public async Task<SubmissionDto> GradeAsync(long submissionId, GradeRequest request, CancellationToken ct = default)
    {
        var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == submissionId, ct) ?? throw new NotFoundException("Submission", submissionId);
        var assignment = await LoadOwnedAsync(submission.AssignmentId, ct);
        if (request.Score > assignment.MaxScore)
        {
            throw new BusinessRuleException($"Score cannot exceed {assignment.MaxScore}.");
        }

        submission.Score = request.Score;
        submission.TeacherFeedback = request.Feedback;
        submission.GradedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        var high = assignment.MaxScore > 0 && request.Score / assignment.MaxScore >= GamificationRules.HighScoreThreshold;
        await gamification.SetAwardAsync(
            submission.StudentUserId, "grade", submission.Id, high ? GamificationRules.HighScoreBonus : null, $"High score: {assignment.Title}", ct);
        await db.SaveChangesAsync(ct);
        return ToDto(submission, assignment, null);
    }

    private long ResolveTeacher(long? requested)
    {
        if (guard.IsStaff || guard.HasPermission(Permissions.Courses.Manage))
        {
            return requested ?? guard.Me;
        }

        if (guard.IsInRole(Roles.Teacher) && (requested is null || requested == guard.Me))
        {
            return guard.Me;
        }

        throw new ForbiddenAccessException("Teachers can only manage their own assignments.");
    }

    private async Task ValidateAsync(SaveAssignmentRequest request, long teacherId, CancellationToken ct)
    {
        if (!await db.Courses.AnyAsync(c => c.Id == request.CourseId, ct))
        {
            throw new NotFoundException(nameof(Course), request.CourseId);
        }

        if (request.StudentUserId is { } student)
        {
            await db.People.EnsureRoleAsync(student, Roles.Student, ct);
        }

        await db.People.EnsureRoleAsync(teacherId, Roles.Teacher, ct);
    }

    private async Task<Assignment> LoadOwnedAsync(long id, CancellationToken ct)
    {
        var assignment = await db.Assignments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException(nameof(Assignment), id);
        if (!(guard.IsStaff || guard.HasPermission(Permissions.Courses.Manage)) && assignment.TeacherUserId != guard.Me)
        {
            throw new ForbiddenAccessException("Only the assignment's teacher or staff can do this.");
        }

        return assignment;
    }

    private static SubmissionDto ToDto(AssignmentSubmission s, Assignment a, string? name) => new(
        s.Id, s.AssignmentId, s.StudentUserId, name, s.Content, s.AttachmentUrl, s.SubmittedAtUtc,
        s.SubmittedAtUtc > a.DueAtUtc, s.Score, s.TeacherFeedback, s.GradedAtUtc);

    private async Task<List<AssignmentDto>> ToDtosAsync(IReadOnlyList<Assignment> list, long? studentId, CancellationToken ct)
    {
        var ids = list.Select(a => a.Id).ToList();
        var counts = await db.Submissions.Where(s => ids.Contains(s.AssignmentId)).GroupBy(s => s.AssignmentId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var mine = studentId is { } sid
            ? await db.Submissions.Where(s => ids.Contains(s.AssignmentId) && s.StudentUserId == sid).ToDictionaryAsync(s => s.AssignmentId, ct)
            : [];
        var courseIds = list.Select(a => a.CourseId).Distinct().ToList();
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var names = await db.People.NamesAsync(
            list.Select(a => a.TeacherUserId).Concat(list.Where(a => a.StudentUserId.HasValue).Select(a => a.StudentUserId!.Value)), ct);

        return list.Select(a => new AssignmentDto(
            a.Id, a.CourseId, courses.GetValueOrDefault(a.CourseId), a.StudentUserId, a.StudentUserId is { } forStudent ? names.GetValueOrDefault(forStudent) : null,
            a.TeacherUserId, names.GetValueOrDefault(a.TeacherUserId), a.Title, a.Description, a.DueAtUtc, a.MaxScore,
            counts.GetValueOrDefault(a.Id), mine.TryGetValue(a.Id, out var s) ? ToDto(s, a, null) : null)).ToList();
    }
}

// ---------- Certificates (US-041) ----------

public sealed record CertificateDto(long Id, long StudentUserId, string? StudentName, long CourseId, string? CourseName, string Number, DateTime IssuedOnUtc);

public sealed record IssueCertificateRequest(long StudentUserId, long CourseId);

public interface ICertificateService
{
    Task<IReadOnlyList<CertificateDto>> ListAsync(long? studentUserId, CancellationToken ct = default);
    Task<CertificateDto> IssueAsync(IssueCertificateRequest request, CancellationToken ct = default);
    Task<(string FileName, byte[] Content)> PdfAsync(long id, CancellationToken ct = default);
}

internal sealed class CertificateService(
    IAcademicDbContext db, AccessGuard guard, IEntitlementsProvider entitlements, ICertificateRenderer renderer, TimeProvider clock)
    : ICertificateService
{
    public async Task<IReadOnlyList<CertificateDto>> ListAsync(long? studentUserId, CancellationToken ct = default)
    {
        var q = db.Certificates.AsNoTracking().AsQueryable();
        if (studentUserId is { } sid)
        {
            await guard.EnsureCanViewStudentAsync(sid, ct);
            q = q.Where(c => c.StudentUserId == sid);
        }
        else if (await guard.VisibleStudentIdsAsync(ct) is { } visible)
        {
            q = q.Where(c => visible.Contains(c.StudentUserId));
        }

        return await ToDtosAsync(await q.OrderByDescending(c => c.IssuedOnUtc).Take(500).ToListAsync(ct), ct);
    }

    /// <summary>Issues once per student and course. Issuing again returns the existing certificate.</summary>
    public async Task<CertificateDto> IssueAsync(IssueCertificateRequest request, CancellationToken ct = default)
    {
        if (!(guard.IsStaff || guard.HasPermission(Permissions.Courses.Manage) || guard.IsInRole(Roles.Teacher)))
        {
            throw new ForbiddenAccessException();
        }

        await entitlements.EnsureFeatureAsync(guard.AcademyId, FeatureKeys.Certificates, ct);
        if (!await db.Students.AnyAsync(s => s.UserId == request.StudentUserId, ct))
        {
            throw new NotFoundException(nameof(Student), request.StudentUserId);
        }

        if (!await db.Courses.AnyAsync(c => c.Id == request.CourseId, ct))
        {
            throw new NotFoundException(nameof(Course), request.CourseId);
        }

        var existing = await db.Certificates.FirstOrDefaultAsync(c => c.StudentUserId == request.StudentUserId && c.CourseId == request.CourseId, ct);
        if (existing is null)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            existing = new Certificate
            {
                StudentUserId = request.StudentUserId,
                CourseId = request.CourseId,
                IssuedOnUtc = now,
                Number = $"CERT-{guard.AcademyId}-{now:yyyyMMdd}-{RandomNumberGenerator.GetHexString(6)}",
            };
            db.Certificates.Add(existing);
            await db.SaveChangesAsync(ct);
        }

        return (await ToDtosAsync([existing], ct))[0];
    }

    public async Task<(string FileName, byte[] Content)> PdfAsync(long id, CancellationToken ct = default)
    {
        var certificate = await db.Certificates.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException(nameof(Certificate), id);
        await guard.EnsureCanViewStudentAsync(certificate.StudentUserId, ct);
        var dto = (await ToDtosAsync([certificate], ct))[0];

        var pdf = renderer.Render(new CertificateDocument(
            dto.StudentName ?? $"#{dto.StudentUserId}", dto.CourseName ?? $"#{dto.CourseId}", dto.Number, dto.IssuedOnUtc, "Academies Platform"));
        return ($"{dto.Number}.pdf", pdf);
    }

    private async Task<List<CertificateDto>> ToDtosAsync(IReadOnlyList<Certificate> list, CancellationToken ct)
    {
        var names = await db.People.NamesAsync(list.Select(c => c.StudentUserId), ct);
        var courseIds = list.Select(c => c.CourseId).Distinct().ToList();
        var courses = await db.Courses.IgnoreQueryFilters().Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return list.Select(c => new CertificateDto(
            c.Id, c.StudentUserId, names.GetValueOrDefault(c.StudentUserId), c.CourseId, courses.GetValueOrDefault(c.CourseId), c.Number, c.IssuedOnUtc)).ToList();
    }
}

internal sealed class SaveAssignmentValidator : AbstractValidator<SaveAssignmentRequest>
{
    public SaveAssignmentValidator()
    {
        RuleFor(x => x.CourseId).GreaterThan(0);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.MaxScore).GreaterThan(0).LessThanOrEqualTo(1000);
    }
}

internal sealed class SubmitValidator : AbstractValidator<SubmitRequest>
{
    public SubmitValidator()
    {
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Content) || !string.IsNullOrWhiteSpace(x.AttachmentUrl))
            .WithMessage("Write an answer or attach a link.");
        RuleFor(x => x.Content).MaximumLength(10000);
        RuleFor(x => x.AttachmentUrl).MaximumLength(1000);
    }
}

internal sealed class GradeValidator : AbstractValidator<GradeRequest>
{
    public GradeValidator()
    {
        RuleFor(x => x.Score).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Feedback).MaximumLength(2000);
    }
}
