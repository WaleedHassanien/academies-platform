using Academies.Academic.Domain;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Academic.Application;

// Sales and customer service: a prospective student (a lead) is followed up, gets a free trial
// session with a teacher in the teacher's room, and is then converted into a student account
// (created in Identity first) or marked lost with a reason.

public sealed record TrialDto(
    long LeadId, string LeadName, string? Phone, string? TimeZone, long? CourseId, string? CourseName, long TeacherUserId, string? TeacherName,
    DateTime StartsAtUtc, DateTime EndsAtUtc, string Status, string? Notes);

public sealed record LeadDto(
    long Id, string FullName, string? Phone, string? Email, string? Country, string? TimeZone, bool IsAdult, string? GuardianName,
    long? CourseId, string? CourseName, string? Source, string Status, string? LostReason, long? AssignedToUserId, string? AssignedToName,
    DateOnly? NextFollowUpOn, string? Notes, TrialDto? Trial, long? ConvertedStudentUserId, string? ConvertedStudentName, DateTime CreatedOnUtc);

public sealed record LeadActivityDto(long Id, string Note, long? ByUserId, string? ByName, DateTime CreatedOnUtc);

public sealed record LeadQuery(string? Search = null, string? Status = null, long? AssignedToUserId = null, int Page = 1, int PageSize = 20);

/// <summary>Counts per status, and how many converted, for the sales dashboard.</summary>
public sealed record LeadSummaryDto(IReadOnlyDictionary<string, int> ByStatus, IReadOnlyDictionary<string, int> BySource, int Total, double ConversionRate);

public sealed record SaveLeadRequest(
    string FullName, string? Phone, string? Email, string? Country, string? TimeZone, bool IsAdult, string? GuardianName, long? CourseId,
    string? Source, long? AssignedToUserId, DateOnly? NextFollowUpOn, string? Notes);

public sealed record SetLeadStatusRequest(LeadStatus Status, string? LostReason);

public sealed record AddLeadNoteRequest(string Note);

public sealed record ScheduleTrialRequest(long TeacherUserId, long? CourseId, DateTime StartsAtUtc, int DurationMinutes = 30);

/// <summary>Attended, NoShow or Cancelled, with the teacher's assessment.</summary>
public sealed record TrialOutcomeRequest(TrialStatus Status, string? Notes);

/// <summary>
/// The student account (and optional guardian and payer accounts) already exist in Identity.
/// Their profiles are filled from the lead: time zone, subject and trial teacher.
/// </summary>
public sealed record ConvertLeadRequest(long StudentUserId, long? ParentUserId = null, long? PayerUserId = null);

public interface ILeadService
{
    Task<PagedResult<LeadDto>> ListAsync(LeadQuery query, CancellationToken ct = default);
    Task<LeadDto> GetAsync(long id, CancellationToken ct = default);
    Task<LeadSummaryDto> SummaryAsync(CancellationToken ct = default);
    Task<LeadDto> CreateAsync(SaveLeadRequest request, CancellationToken ct = default);
    Task<LeadDto> UpdateAsync(long id, SaveLeadRequest request, CancellationToken ct = default);
    Task<LeadDto> SetStatusAsync(long id, SetLeadStatusRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<LeadActivityDto>> ActivityAsync(long id, CancellationToken ct = default);
    Task<LeadActivityDto> AddNoteAsync(long id, AddLeadNoteRequest request, CancellationToken ct = default);
    Task<LeadDto> ScheduleTrialAsync(long id, ScheduleTrialRequest request, CancellationToken ct = default);

    /// <summary>The trial's teacher (or sales) records how it went.</summary>
    Task<LeadDto> RecordTrialOutcomeAsync(long id, TrialOutcomeRequest request, CancellationToken ct = default);

    /// <summary>A guest link into the teacher's room for the prospective student, valid around the trial.</summary>
    Task<JoinLinkDto> TrialJoinLinkAsync(long id, CancellationToken ct = default);

    Task<LeadDto> ConvertAsync(long id, ConvertLeadRequest request, CancellationToken ct = default);

    /// <summary>The calling teacher's trials that are upcoming or still waiting for an assessment.</summary>
    Task<IReadOnlyList<TrialDto>> MyTrialsAsync(CancellationToken ct = default);
}

internal sealed class LeadService(
    IAcademicDbContext db, AccessGuard guard, IMeetingLinkGenerator meetings, IEventPublisher events, TimeProvider clock) : ILeadService
{
    public async Task<PagedResult<LeadDto>> ListAsync(LeadQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.Leads.AsNoTracking().AsQueryable();

        if (Enum.TryParse<LeadStatus>(query.Status, true, out var status))
        {
            q = q.Where(l => l.Status == status);
        }

        if (query.AssignedToUserId is { } assigned)
        {
            q = q.Where(l => l.AssignedToUserId == assigned);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(l => l.FullName.Contains(s) || (l.Phone != null && l.Phone.Contains(s)) || (l.Email != null && l.Email.Contains(s))
                             || (l.GuardianName != null && l.GuardianName.Contains(s)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(l => l.Id).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<LeadDto>
        {
            Items = await ToDtosAsync(rows, ct), Page = page.SafePage, PageSize = page.SafePageSize, TotalCount = total,
        };
    }

    public async Task<LeadDto> GetAsync(long id, CancellationToken ct = default) =>
        (await ToDtosAsync([await LoadAsync(id, ct)], ct))[0];

    public async Task<LeadSummaryDto> SummaryAsync(CancellationToken ct = default)
    {
        var rows = await db.Leads.AsNoTracking().Select(l => new { l.Status, l.Source }).ToListAsync(ct);
        var closed = rows.Count(r => r.Status is LeadStatus.Converted or LeadStatus.Lost);
        var converted = rows.Count(r => r.Status == LeadStatus.Converted);
        return new LeadSummaryDto(
            rows.GroupBy(r => r.Status.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            rows.GroupBy(r => string.IsNullOrWhiteSpace(r.Source) ? "Other" : r.Source!).ToDictionary(g => g.Key, g => g.Count()),
            rows.Count,
            closed == 0 ? 0 : Math.Round(converted * 100.0 / closed, 1));
    }

    public async Task<LeadDto> CreateAsync(SaveLeadRequest request, CancellationToken ct = default)
    {
        await ValidateAsync(request, ct);
        var lead = new Lead { FullName = request.FullName.Trim(), AssignedToUserId = request.AssignedToUserId ?? guard.Me };
        Apply(lead, request);
        db.Leads.Add(lead);
        await db.SaveChangesAsync(ct);
        return await GetAsync(lead.Id, ct);
    }

    public async Task<LeadDto> UpdateAsync(long id, SaveLeadRequest request, CancellationToken ct = default)
    {
        await ValidateAsync(request, ct);
        var lead = await LoadAsync(id, ct);
        Apply(lead, request);
        lead.AssignedToUserId = request.AssignedToUserId ?? lead.AssignedToUserId;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<LeadDto> SetStatusAsync(long id, SetLeadStatusRequest request, CancellationToken ct = default)
    {
        var lead = await LoadAsync(id, ct);
        if (lead.Status == LeadStatus.Converted)
        {
            throw new BusinessRuleException("This lead is already a student.");
        }

        if (request.Status == LeadStatus.Converted)
        {
            throw new BusinessRuleException("Convert the lead with a student account instead.");
        }

        if (request.Status == LeadStatus.Lost && string.IsNullOrWhiteSpace(request.LostReason))
        {
            throw new BusinessRuleException("Say why the lead was lost.");
        }

        lead.Status = request.Status;
        lead.LostReason = request.Status == LeadStatus.Lost ? request.LostReason!.Trim() : null;
        if (request.Status == LeadStatus.Lost && lead.TrialStatus == TrialStatus.Scheduled)
        {
            lead.TrialStatus = TrialStatus.Cancelled;
        }

        db.LeadActivities.Add(new LeadActivity { LeadId = id, Note = $"Status: {request.Status}" + (lead.LostReason is { } r ? $" — {r}" : "") });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var lead = await LoadAsync(id, ct);
        if (lead.Status == LeadStatus.Converted)
        {
            throw new BusinessRuleException("A converted lead is kept for the record.");
        }

        db.Leads.Remove(lead);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LeadActivityDto>> ActivityAsync(long id, CancellationToken ct = default)
    {
        _ = await LoadAsync(id, ct);
        var rows = await db.LeadActivities.AsNoTracking().Where(a => a.LeadId == id).OrderByDescending(a => a.Id).Take(200).ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.Where(r => r.CreatedBy.HasValue).Select(r => r.CreatedBy!.Value), ct);
        return rows.Select(a => new LeadActivityDto(a.Id, a.Note, a.CreatedBy, a.CreatedBy is { } by ? names.GetValueOrDefault(by) : null, a.CreatedOnUtc))
            .ToList();
    }

    public async Task<LeadActivityDto> AddNoteAsync(long id, AddLeadNoteRequest request, CancellationToken ct = default)
    {
        var lead = await LoadAsync(id, ct);
        var note = new LeadActivity { LeadId = id, Note = request.Note.Trim() };
        db.LeadActivities.Add(note);
        if (lead.Status == LeadStatus.New)
        {
            lead.Status = LeadStatus.Contacted;
        }

        await db.SaveChangesAsync(ct);
        return (await ActivityAsync(id, ct)).First(a => a.Id == note.Id);
    }

    public async Task<LeadDto> ScheduleTrialAsync(long id, ScheduleTrialRequest request, CancellationToken ct = default)
    {
        var lead = await LoadAsync(id, ct);
        if (lead.Status is LeadStatus.Converted or LeadStatus.Lost)
        {
            throw new BusinessRuleException("This lead is closed.");
        }

        var courseId = request.CourseId ?? lead.CourseId;
        await db.People.EnsureRoleAsync(request.TeacherUserId, Roles.Teacher, ct);
        if (courseId is { } course)
        {
            await db.EnsureTeacherQualifiedAsync(request.TeacherUserId, course, ct);
        }

        var start = request.StartsAtUtc;
        var end = start.AddMinutes(request.DurationMinutes);
        await EnsureTeacherFreeAsync(lead.Id, request.TeacherUserId, start, end, ct);

        lead.CourseId = courseId;
        lead.TrialTeacherUserId = request.TeacherUserId;
        lead.TrialStartsAtUtc = start;
        lead.TrialEndsAtUtc = end;
        lead.TrialStatus = TrialStatus.Scheduled;
        lead.TrialNotes = null;
        lead.Status = LeadStatus.TrialScheduled;
        db.LeadActivities.Add(new LeadActivity { LeadId = id, Note = $"Trial scheduled for {start:yyyy-MM-dd HH:mm} UTC" });
        await db.SaveChangesAsync(ct);

        await events.PublishAsync(new SessionReminderDue(
            lead.AcademyId, 0, $"Trial: {lead.FullName}", start, [request.TeacherUserId]), ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<LeadDto> RecordTrialOutcomeAsync(long id, TrialOutcomeRequest request, CancellationToken ct = default)
    {
        var lead = await LoadAsync(id, ct);
        if (lead.TrialTeacherUserId is not { } teacher || lead.TrialStatus is null)
        {
            throw new BusinessRuleException("This lead has no trial session.");
        }

        if (!guard.HasPermission(Permissions.Leads.Manage) && !guard.IsStaff && teacher != guard.Me)
        {
            throw new ForbiddenAccessException("Only the trial's teacher or sales can record how it went.");
        }

        if (request.Status == TrialStatus.Scheduled)
        {
            throw new BusinessRuleException("Choose attended, no-show or cancelled.");
        }

        if (request.Status is TrialStatus.Attended or TrialStatus.NoShow && lead.TrialStartsAtUtc > clock.GetUtcNow().UtcDateTime)
        {
            throw new BusinessRuleException("The trial hasn't started yet.");
        }

        lead.TrialStatus = request.Status;
        lead.TrialNotes = string.IsNullOrWhiteSpace(request.Notes) ? lead.TrialNotes : request.Notes.Trim();
        if (lead.Status is not (LeadStatus.Converted or LeadStatus.Lost))
        {
            lead.Status = request.Status == TrialStatus.Attended ? LeadStatus.TrialDone : LeadStatus.Contacted;
        }

        db.LeadActivities.Add(new LeadActivity { LeadId = id, Note = $"Trial: {request.Status}" + (request.Notes is { Length: > 0 } n ? $" — {n.Trim()}" : "") });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<JoinLinkDto> TrialJoinLinkAsync(long id, CancellationToken ct = default)
    {
        var lead = await LoadAsync(id, ct);
        if (lead is not { TrialStatus: TrialStatus.Scheduled, TrialTeacherUserId: { } teacher, TrialStartsAtUtc: { } start, TrialEndsAtUtc: { } end })
        {
            throw new BusinessRuleException("This lead has no upcoming trial session.");
        }

        var opens = start.AddMinutes(-30);
        var closes = end.AddMinutes(15);
        var url = meetings.JoinUrl(guard.AcademyId, teacher, new MeetingParticipant(0, lead.FullName, lead.Email ?? string.Empty, false), opens, closes);
        return new JoinLinkDto(url, false, closes);
    }

    public async Task<LeadDto> ConvertAsync(long id, ConvertLeadRequest request, CancellationToken ct = default)
    {
        var lead = await LoadAsync(id, ct);
        if (lead.Status == LeadStatus.Converted)
        {
            throw new ConflictException("This lead is already a student.");
        }

        // The accounts were just created in Identity, so the people directory may not have them yet;
        // an id that is known must have the right role.
        await EnsureRoleIfKnownAsync(request.StudentUserId, Roles.Student, ct);
        if (request.ParentUserId is { } parent)
        {
            await EnsureRoleIfKnownAsync(parent, Roles.Parent, ct);
        }

        if (request.PayerUserId is { } payer && payer != request.StudentUserId)
        {
            await EnsureRoleIfKnownAsync(payer, Roles.Parent, ct);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var student = await db.Students.FirstOrDefaultAsync(s => s.UserId == request.StudentUserId, ct);
        if (student is null)
        {
            student = new Student { UserId = request.StudentUserId, EnrollmentDate = today };
            db.Students.Add(student);
        }

        student.TimeZone ??= lead.TimeZone;
        if (request.ParentUserId is { } guardian && student.ParentUserId != guardian)
        {
            student.ParentUserId = guardian;
            await events.PublishAsync(new StudentParentChanged(guard.AcademyId, request.StudentUserId, guardian), ct);
        }

        if (request.PayerUserId is { } chosen && student.PayerUserId != chosen)
        {
            student.PayerUserId = chosen;
            await events.PublishAsync(new StudentPayerChanged(guard.AcademyId, request.StudentUserId, chosen), ct);
        }

        if (lead.CourseId is { } course)
        {
            await db.EnsureEnrollmentAsync(request.StudentUserId, course, lead.TrialTeacherUserId, today, ct);
        }

        if (lead.TrialTeacherUserId is { } teacher)
        {
            await db.EnsureTeacherLinkAsync(teacher, request.StudentUserId, ct);
        }

        lead.Status = LeadStatus.Converted;
        lead.ConvertedStudentUserId = request.StudentUserId;
        lead.ConvertedOnUtc = clock.GetUtcNow().UtcDateTime;
        db.LeadActivities.Add(new LeadActivity { LeadId = id, Note = "Converted into a student" });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<IReadOnlyList<TrialDto>> MyTrialsAsync(CancellationToken ct = default)
    {
        var me = guard.Me;
        var since = clock.GetUtcNow().UtcDateTime.AddDays(-7);
        var rows = await db.Leads.AsNoTracking()
            .Where(l => l.TrialTeacherUserId == me && l.TrialStatus == TrialStatus.Scheduled && l.TrialStartsAtUtc >= since)
            .OrderBy(l => l.TrialStartsAtUtc)
            .Take(50)
            .ToListAsync(ct);
        return (await ToDtosAsync(rows, ct)).Select(l => l.Trial!).ToList();
    }

    // ---- helpers ----

    private async Task EnsureTeacherFreeAsync(long leadId, long teacherUserId, DateTime start, DateTime end, CancellationToken ct)
    {
        var session = await db.Sessions.AsNoTracking()
            .Where(s => s.TeacherUserId == teacherUserId && s.Status != SessionStatus.Cancelled && s.Status != SessionStatus.Excused)
            .Where(s => s.StartsAtUtc < end && start < s.EndsAtUtc)
            .Select(s => new { s.Title, s.StartsAtUtc })
            .FirstOrDefaultAsync(ct);
        if (session is not null)
        {
            throw new ConflictException($"The teacher already has '{session.Title}' at {session.StartsAtUtc:yyyy-MM-dd HH:mm} UTC.");
        }

        var trial = await db.Leads.AsNoTracking()
            .Where(l => l.Id != leadId && l.TrialTeacherUserId == teacherUserId && l.TrialStatus == TrialStatus.Scheduled)
            .Where(l => l.TrialStartsAtUtc < end && start < l.TrialEndsAtUtc)
            .Select(l => l.TrialStartsAtUtc)
            .FirstOrDefaultAsync(ct);
        if (trial is { } at)
        {
            throw new ConflictException($"The teacher already has a trial session at {at:yyyy-MM-dd HH:mm} UTC.");
        }
    }

    private async Task EnsureRoleIfKnownAsync(long userId, string role, CancellationToken ct)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (person is not null && !person.HasRole(role))
        {
            throw new BusinessRuleException($"User {userId} is not a {role} in this academy.");
        }
    }

    private async Task ValidateAsync(SaveLeadRequest request, CancellationToken ct)
    {
        if (request.CourseId is { } course && !await db.Courses.AnyAsync(c => c.Id == course, ct))
        {
            throw new NotFoundException(nameof(Course), course);
        }
    }

    private static void Apply(Lead lead, SaveLeadRequest r)
    {
        static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        lead.FullName = r.FullName.Trim();
        lead.Phone = Clean(r.Phone);
        lead.Email = Clean(r.Email);
        lead.Country = Clean(r.Country);
        lead.TimeZone = Clean(r.TimeZone);
        lead.IsAdult = r.IsAdult;
        lead.GuardianName = r.IsAdult ? null : Clean(r.GuardianName);
        lead.CourseId = r.CourseId;
        lead.Source = Clean(r.Source);
        lead.NextFollowUpOn = r.NextFollowUpOn;
        lead.Notes = Clean(r.Notes);
    }

    private async Task<Lead> LoadAsync(long id, CancellationToken ct) =>
        await db.Leads.FirstOrDefaultAsync(l => l.Id == id, ct) ?? throw new NotFoundException(nameof(Lead), id);

    private async Task<List<LeadDto>> ToDtosAsync(IReadOnlyList<Lead> leads, CancellationToken ct)
    {
        var courseIds = leads.Where(l => l.CourseId.HasValue).Select(l => l.CourseId!.Value).Distinct().ToList();
        var courses = await db.Courses.Where(c => courseIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var people = await db.People.NamesAsync(leads.SelectMany(l => new[] { l.AssignedToUserId, l.TrialTeacherUserId, l.ConvertedStudentUserId })
            .Where(x => x.HasValue).Select(x => x!.Value), ct);

        return leads.Select(l =>
        {
            var course = l.CourseId is { } c ? courses.GetValueOrDefault(c) : null;
            var trial = l is { TrialTeacherUserId: { } teacher, TrialStartsAtUtc: { } start, TrialEndsAtUtc: { } end, TrialStatus: { } status }
                ? new TrialDto(l.Id, l.FullName, l.Phone, l.TimeZone, l.CourseId, course, teacher, people.GetValueOrDefault(teacher), start, end,
                    status.ToString(), l.TrialNotes)
                : null;
            return new LeadDto(
                l.Id, l.FullName, l.Phone, l.Email, l.Country, l.TimeZone, l.IsAdult, l.GuardianName, l.CourseId, course, l.Source, l.Status.ToString(),
                l.LostReason, l.AssignedToUserId, l.AssignedToUserId is { } a ? people.GetValueOrDefault(a) : null, l.NextFollowUpOn, l.Notes, trial,
                l.ConvertedStudentUserId, l.ConvertedStudentUserId is { } s ? people.GetValueOrDefault(s) : null, l.CreatedOnUtc);
        }).ToList();
    }
}

internal sealed class SaveLeadValidator : AbstractValidator<SaveLeadRequest>
{
    public SaveLeadValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Phone).MaximumLength(30);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x).Must(x => !string.IsNullOrWhiteSpace(x.Phone) || !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Enter a phone number or an email to reach them.");
        RuleFor(x => x.Country).MaximumLength(80);
        RuleFor(x => x.TimeZone).MaximumLength(64)
            .Must(tz => (tz!.Contains('/') || tz.Trim() == "UTC") && TimeZoneInfo.TryFindSystemTimeZoneById(tz.Trim(), out _))
            .WithMessage("Choose a valid time zone, such as Asia/Riyadh.")
            .When(x => !string.IsNullOrWhiteSpace(x.TimeZone));
        RuleFor(x => x.GuardianName).MaximumLength(200);
        RuleFor(x => x.Source).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

internal sealed class SetLeadStatusValidator : AbstractValidator<SetLeadStatusRequest>
{
    public SetLeadStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.LostReason).MaximumLength(500);
    }
}

internal sealed class AddLeadNoteValidator : AbstractValidator<AddLeadNoteRequest>
{
    public AddLeadNoteValidator() => RuleFor(x => x.Note).NotEmpty().MaximumLength(2000);
}

internal sealed class ScheduleTrialValidator : AbstractValidator<ScheduleTrialRequest>
{
    public ScheduleTrialValidator()
    {
        RuleFor(x => x.TeacherUserId).GreaterThan(0);
        RuleFor(x => x.DurationMinutes).InclusiveBetween(15, 120);
    }
}

internal sealed class TrialOutcomeValidator : AbstractValidator<TrialOutcomeRequest>
{
    public TrialOutcomeValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

internal sealed class ConvertLeadValidator : AbstractValidator<ConvertLeadRequest>
{
    public ConvertLeadValidator() => RuleFor(x => x.StudentUserId).GreaterThan(0);
}
