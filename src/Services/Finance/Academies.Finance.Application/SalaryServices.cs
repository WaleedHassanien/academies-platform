using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Finance.Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Finance.Application;

// ---------- Pay settings (US-031) ----------

public sealed record SetCompensationRequest(PayType PayType, decimal Amount, DateOnly EffectiveFrom, string? Note);

public sealed record CompensationDto(long Id, long UserId, string PayType, decimal Amount, DateOnly EffectiveFrom, string? Note, DateTime CreatedOnUtc);

public sealed record StaffPayDto(long UserId, string FullName, string Roles, bool IsActive, CompensationDto? Current);

public interface ICompensationService
{
    Task<IReadOnlyList<StaffPayDto>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CompensationDto>> HistoryAsync(long userId, CancellationToken ct = default);
    Task<CompensationDto> SetAsync(long userId, SetCompensationRequest request, CancellationToken ct = default);
}

internal sealed class CompensationService(IFinanceDbContext db, IAuditTrail audit, TimeProvider clock) : ICompensationService
{
    private static readonly string[] PaidRoles = [Roles.Teacher, Roles.Supervisor, Roles.Staff, Roles.Accountant, Roles.Manager];

    /// <summary>Everyone who can be paid, with the setting in force today.</summary>
    public async Task<IReadOnlyList<StaffPayDto>> ListAsync(CancellationToken ct = default)
    {
        var people = (await db.People.AsNoTracking().ToListAsync(ct)).Where(p => PaidRoles.Any(p.HasRole)).ToList();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var ids = people.Select(p => p.UserId).ToList();
        var all = await db.Compensations.AsNoTracking().Where(c => ids.Contains(c.UserId) && c.EffectiveFrom <= today).ToListAsync(ct);

        return people.OrderBy(p => p.FullName).Select(p => new StaffPayDto(
            p.UserId, p.FullName, p.Roles, p.IsActive,
            all.Where(c => c.UserId == p.UserId).OrderByDescending(c => c.EffectiveFrom).ThenByDescending(c => c.Id).Select(ToDto).FirstOrDefault()))
            .ToList();
    }

    public async Task<IReadOnlyList<CompensationDto>> HistoryAsync(long userId, CancellationToken ct = default) =>
        (await db.Compensations.AsNoTracking().Where(c => c.UserId == userId).OrderByDescending(c => c.EffectiveFrom).ToListAsync(ct))
            .Select(ToDto).ToList();

    /// <summary>Teachers are paid per session; supervisors and staff a fixed monthly salary (US-031).</summary>
    public async Task<CompensationDto> SetAsync(long userId, SetCompensationRequest request, CancellationToken ct = default)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.UserId == userId, ct)
            ?? throw new NotFoundException("User", userId);

        var allowed = request.PayType == PayType.PerSession
            ? person.HasRole(Roles.Teacher)
            : PaidRoles.Where(r => r != Roles.Teacher).Any(person.HasRole);
        if (!allowed)
        {
            throw new BusinessRuleException(request.PayType == PayType.PerSession
                ? "Only teachers are paid per session."
                : "A fixed monthly salary applies to supervisors and staff.");
        }

        var row = await db.Compensations.FirstOrDefaultAsync(c => c.UserId == userId && c.EffectiveFrom == request.EffectiveFrom, ct);
        if (row is null)
        {
            row = new Compensation { UserId = userId, EffectiveFrom = request.EffectiveFrom };
            db.Compensations.Add(row);
        }

        row.PayType = request.PayType;
        row.Amount = request.Amount;
        row.Note = request.Note;
        await audit.RecordAsync("salaries.compensation", nameof(Compensation), userId, request, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    private static CompensationDto ToDto(Compensation c) => new(c.Id, c.UserId, c.PayType.ToString(), c.Amount, c.EffectiveFrom, c.Note, c.CreatedOnUtc);
}

// ---------- Salaries (US-032, US-033) ----------

public sealed record SalaryDto(
    long Id, long UserId, string? FullName, string Role, int Year, int Month, string PayType, int? SessionsCount,
    decimal? RatePerSession, decimal Amount, string Status, DateTime? PaidOnUtc, string? Note);

public sealed record GenerateResultDto(int Year, int Month, int Created, int Regenerated, int Unchanged, int SkippedPaid, IReadOnlyList<SalaryDto> Salaries);

public sealed record AdjustSalaryRequest(decimal Amount, string Note);

public sealed record SalaryLogDto(
    long Id, long SalaryId, long UserId, string? FullName, string Role, int Year, int Month, string PayType, int? SessionsCount,
    decimal? RatePerSession, decimal Amount, string Action, long? PaidByUserId, string? Note, DateTime CreatedAt);

public sealed record SalaryLogQuery(long? UserId = null, int? Year = null, int Page = 1, int PageSize = 50);

public interface ISalaryService
{
    Task<GenerateResultDto> GenerateAsync(int year, int month, CancellationToken ct = default);
    Task<IReadOnlyList<SalaryDto>> ListAsync(int year, int month, CancellationToken ct = default);
    Task<SalaryDto> PayAsync(long id, CancellationToken ct = default);
    Task<SalaryDto> AdjustAsync(long id, AdjustSalaryRequest request, CancellationToken ct = default);
    Task<PagedResult<SalaryLogDto>> LogsAsync(SalaryLogQuery query, CancellationToken ct = default);
    Task<PagedResult<SalaryLogDto>> MyLogsAsync(int page, int pageSize, CancellationToken ct = default);
}

internal sealed class SalaryService(
    IFinanceDbContext db,
    IAcademicClient academic,
    FinanceAccess access,
    IReportCache reports,
    IEventPublisher events,
    IAuditTrail audit,
    TimeProvider clock) : ISalaryService
{
    /// <summary>
    /// Builds the month's salaries (US-032). Fixed: the monthly amount. Per session: completed
    /// sessions × rate. Months not yet paid are recalculated; paid months change only through a
    /// documented adjustment.
    /// </summary>
    public async Task<GenerateResultDto> GenerateAsync(int year, int month, CancellationToken ct = default)
    {
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var settings = await db.Compensations.AsNoTracking().Where(c => c.EffectiveFrom <= monthEnd).ToListAsync(ct);
        var inForce = settings.GroupBy(c => c.UserId)
            .Select(g => g.OrderByDescending(c => c.EffectiveFrom).ThenByDescending(c => c.Id).First())
            .ToList();

        var needsSessions = inForce.Any(c => c.PayType == PayType.PerSession);
        var sessions = needsSessions
            ? (await academic.CompletedSessionCountsAsync(access.AcademyId, year, month, ct)).ToDictionary(s => s.TeacherUserId, s => s.CompletedSessions)
            : [];
        var people = await db.People.Where(p => inForce.Select(c => c.UserId).Contains(p.UserId)).ToDictionaryAsync(p => p.UserId, ct);
        var existing = await db.Salaries.Where(s => s.Year == year && s.Month == month).ToDictionaryAsync(s => s.UserId, ct);

        int created = 0, regenerated = 0, unchanged = 0, skipped = 0;
        var touched = new List<(Salary Salary, SalaryAction Action)>();

        foreach (var setting in inForce)
        {
            var person = people.GetValueOrDefault(setting.UserId);
            if (person is null || !person.IsActive)
            {
                continue;
            }

            var count = setting.PayType == PayType.PerSession ? sessions.GetValueOrDefault(setting.UserId) : (int?)null;
            var amount = SalaryCalculator.Calculate(setting.PayType, setting.Amount, count ?? 0);
            var role = setting.PayType == PayType.PerSession ? Roles.Teacher : person.Roles.Split(',').FirstOrDefault(r => r != Roles.Teacher) ?? Roles.Staff;

            if (existing.TryGetValue(setting.UserId, out var salary))
            {
                if (salary.Status == SalaryStatus.Paid)
                {
                    skipped++;
                    continue;
                }

                if (salary.Amount == amount && salary.SessionsCount == count && salary.PayType == setting.PayType)
                {
                    unchanged++;
                    continue;
                }

                Apply(salary, setting, count, amount, role);
                touched.Add((salary, SalaryAction.Regenerated));
                regenerated++;
            }
            else
            {
                salary = new Salary { UserId = setting.UserId, Year = year, Month = month };
                Apply(salary, setting, count, amount, role);
                db.Salaries.Add(salary);
                touched.Add((salary, SalaryAction.Created));
                created++;
            }
        }

        await db.SaveChangesAsync(ct);
        foreach (var (salary, action) in touched)
        {
            db.SalaryLogs.Add(Log(salary, action, null, null));
        }

        await audit.RecordAsync("salaries.generate", nameof(Salary), $"{year}-{month:00}", new { created, regenerated }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);

        return new GenerateResultDto(year, month, created, regenerated, unchanged, skipped, await ListAsync(year, month, ct));
    }

    public async Task<IReadOnlyList<SalaryDto>> ListAsync(int year, int month, CancellationToken ct = default)
    {
        var rows = await db.Salaries.AsNoTracking().Where(s => s.Year == year && s.Month == month).ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.Select(r => r.UserId), ct);
        return rows.Select(s => ToDto(s, names.GetValueOrDefault(s.UserId))).OrderBy(s => s.FullName).ToList();
    }

    public async Task<SalaryDto> PayAsync(long id, CancellationToken ct = default)
    {
        var salary = await LoadAsync(id, ct);
        if (salary.Status == SalaryStatus.Paid)
        {
            throw new BusinessRuleException("This salary is already paid.");
        }

        salary.Status = SalaryStatus.Paid;
        salary.PaidOnUtc = clock.GetUtcNow().UtcDateTime;
        salary.PaidByUserId = access.Me;
        db.SalaryLogs.Add(Log(salary, SalaryAction.Paid, access.Me, null));
        await events.PublishAsync(new SalaryPaid(salary.AcademyId, salary.Id, salary.UserId, salary.Year, salary.Month, salary.Amount), ct);
        await audit.RecordAsync("salaries.pay", nameof(Salary), id, new { salary.Amount }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(salary.AcademyId, ct);
        return ToDto(salary, null);
    }

    /// <summary>The only way to change a paid salary, and it always needs a note (US-032).</summary>
    public async Task<SalaryDto> AdjustAsync(long id, AdjustSalaryRequest request, CancellationToken ct = default)
    {
        var salary = await LoadAsync(id, ct);
        var before = salary.Amount;
        salary.Amount = request.Amount;
        salary.Note = request.Note;
        db.SalaryLogs.Add(Log(salary, SalaryAction.Adjusted, access.Me, $"{before:0.00} → {request.Amount:0.00}: {request.Note}"));
        await audit.RecordAsync("salaries.adjust", nameof(Salary), id, new { before, after = request.Amount, request.Note }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(salary.AcademyId, ct);
        return ToDto(salary, null);
    }

    /// <summary>Admins and accountants see everyone's logs (US-033).</summary>
    public async Task<PagedResult<SalaryLogDto>> LogsAsync(SalaryLogQuery query, CancellationToken ct = default)
    {
        if (!access.SeesAllSalaries)
        {
            throw new ForbiddenAccessException("Only admins and accountants see all salary logs.");
        }

        var q = db.SalaryLogs.AsNoTracking().AsQueryable();
        if (query.UserId is { } uid)
        {
            q = q.Where(l => l.UserId == uid);
        }

        if (query.Year is { } year)
        {
            q = q.Where(l => l.Year == year);
        }

        return await PageAsync(q, query.Page, query.PageSize, ct);
    }

    /// <summary>A teacher, supervisor or staff member sees only their own log (US-033).</summary>
    public Task<PagedResult<SalaryLogDto>> MyLogsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        var me = access.Me;
        return PageAsync(db.SalaryLogs.AsNoTracking().Where(l => l.UserId == me), page, pageSize, ct);
    }

    private async Task<PagedResult<SalaryLogDto>> PageAsync(IQueryable<SalaryLog> q, int pageNumber, int pageSize, CancellationToken ct)
    {
        var page = new PageRequest(pageNumber, pageSize);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(l => l.Year).ThenByDescending(l => l.Month).ThenByDescending(l => l.Id)
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.Select(r => r.UserId), ct);
        var items = rows.Select(l => new SalaryLogDto(
            l.Id, l.SalaryId, l.UserId, names.GetValueOrDefault(l.UserId), l.Role, l.Year, l.Month, l.PayType.ToString(), l.SessionsCount,
            l.RatePerSession, l.Amount, l.Action.ToString(), l.PaidByUserId, l.Note, l.CreatedOnUtc)).ToList();
        return new PagedResult<SalaryLogDto> { Items = items, Page = page.SafePage, PageSize = page.SafePageSize, TotalCount = total };
    }

    private static void Apply(Salary salary, Compensation setting, int? count, decimal amount, string role)
    {
        salary.PayType = setting.PayType;
        salary.SessionsCount = count;
        salary.RatePerSession = setting.PayType == PayType.PerSession ? setting.Amount : null;
        salary.Amount = amount;
        salary.Role = role;
    }

    private static SalaryLog Log(Salary s, SalaryAction action, long? paidBy, string? note) => new()
    {
        SalaryId = s.Id, UserId = s.UserId, Role = s.Role, Year = s.Year, Month = s.Month, PayType = s.PayType, SessionsCount = s.SessionsCount,
        RatePerSession = s.RatePerSession, Amount = s.Amount, Action = action, PaidByUserId = paidBy, Note = note,
    };

    private async Task<Salary> LoadAsync(long id, CancellationToken ct) =>
        await db.Salaries.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException(nameof(Salary), id);

    private static SalaryDto ToDto(Salary s, string? name) => new(
        s.Id, s.UserId, name, s.Role, s.Year, s.Month, s.PayType.ToString(), s.SessionsCount, s.RatePerSession, s.Amount,
        s.Status.ToString(), s.PaidOnUtc, s.Note);
}

// ---------- Expenses and reports (US-034) ----------

public sealed record SaveExpenseRequest(string Category, string? Description, decimal Amount, DateOnly SpentOn, string? Reference);

public sealed record ExpenseDto(long Id, string Category, string? Description, decimal Amount, DateOnly SpentOn, string? Reference, DateTime CreatedOnUtc);

public sealed record MonthlyFinanceDto(string Month, decimal Revenue, decimal Salaries, decimal Expenses, decimal Net);

public sealed record FinanceSummaryDto(
    DateOnly From, DateOnly To, decimal Revenue, decimal Refunds, decimal Salaries, decimal Expenses, decimal Net, decimal Outstanding,
    IReadOnlyList<MonthlyFinanceDto> Monthly, IReadOnlyDictionary<string, decimal> ExpensesByCategory, string Currency);

public interface IExpenseService
{
    Task<IReadOnlyList<ExpenseDto>> ListAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<ExpenseDto> CreateAsync(SaveExpenseRequest request, CancellationToken ct = default);
    Task<ExpenseDto> UpdateAsync(long id, SaveExpenseRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
}

internal sealed class ExpenseService(IFinanceDbContext db, FinanceAccess access, IReportCache reports, IAuditTrail audit) : IExpenseService
{
    public async Task<IReadOnlyList<ExpenseDto>> ListAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default) =>
        (await db.Expenses.AsNoTracking()
            .Where(e => (from == null || e.SpentOn >= from) && (to == null || e.SpentOn <= to))
            .OrderByDescending(e => e.SpentOn).Take(1000).ToListAsync(ct))
        .Select(ToDto).ToList();

    public async Task<ExpenseDto> CreateAsync(SaveExpenseRequest request, CancellationToken ct = default)
    {
        var expense = new Expense { Category = request.Category.Trim() };
        Apply(expense, request);
        db.Expenses.Add(expense);
        await audit.RecordAsync("expenses.create", nameof(Expense), null, request, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);
        return ToDto(expense);
    }

    public async Task<ExpenseDto> UpdateAsync(long id, SaveExpenseRequest request, CancellationToken ct = default)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException(nameof(Expense), id);
        Apply(expense, request);
        await audit.RecordAsync("expenses.update", nameof(Expense), id, request, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);
        return ToDto(expense);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException(nameof(Expense), id);
        db.Expenses.Remove(expense);
        await audit.RecordAsync("expenses.delete", nameof(Expense), id, null, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);
    }

    private static void Apply(Expense e, SaveExpenseRequest r)
    {
        e.Category = r.Category.Trim();
        e.Description = r.Description;
        e.Amount = r.Amount;
        e.SpentOn = r.SpentOn;
        e.Reference = r.Reference;
    }

    private static ExpenseDto ToDto(Expense e) => new(e.Id, e.Category, e.Description, e.Amount, e.SpentOn, e.Reference, e.CreatedOnUtc);
}

public interface IReportService
{
    Task<FinanceSummaryDto> SummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>Net profit = revenue − salaries − expenses, per period and per month, cached in Redis (US-034).</summary>
internal sealed class ReportService(
    IFinanceDbContext db, FinanceAccess access, IReportCache cache, IFinanceSettingsService settings, TimeProvider clock) : IReportService
{
    public Task<FinanceSummaryDto> SummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 800)
        {
            throw new BusinessRuleException("Choose a period of up to about two years.");
        }

        return cache.GetOrCreateAsync(access.AcademyId, $"summary:{from:yyyyMMdd}:{to:yyyyMMdd}", token => ComputeAsync(from, to, token), ct);
    }

    private async Task<FinanceSummaryDto> ComputeAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var logs = await db.PaymentLogs.AsNoTracking()
            .Where(l => l.CreatedOnUtc >= fromUtc && l.CreatedOnUtc < toUtc
                        && (l.Action == PaymentAction.Paid || l.Action == PaymentAction.PartiallyPaid || l.Action == PaymentAction.Refunded))
            .Select(l => new { l.Action, l.Amount, l.CreatedOnUtc })
            .ToListAsync(ct);
        var salaries = await db.Salaries.AsNoTracking()
            .Where(s => s.Status == SalaryStatus.Paid && s.PaidOnUtc >= fromUtc && s.PaidOnUtc < toUtc)
            .Select(s => new { s.Amount, PaidOn = s.PaidOnUtc!.Value })
            .ToListAsync(ct);
        var expenses = await db.Expenses.AsNoTracking()
            .Where(e => e.SpentOn >= from && e.SpentOn <= to)
            .Select(e => new { e.Amount, e.SpentOn, e.Category })
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var outstanding = await db.StudentPayments.AsNoTracking()
            .Where(p => p.Status != PaymentStatus.Cancelled && p.Status != PaymentStatus.Paid && p.DueDate <= today)
            .SumAsync(p => (decimal?)(p.Amount - p.PaidAmount), ct) ?? 0;

        var monthly = new List<MonthlyFinanceDto>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var start = m.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var end = m.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var revenue = logs.Where(l => l.CreatedOnUtc >= start && l.CreatedOnUtc < end)
                .Sum(l => l.Action == PaymentAction.Refunded ? -l.Amount : l.Amount);
            var paidSalaries = salaries.Where(s => s.PaidOn >= start && s.PaidOn < end).Sum(s => s.Amount);
            var spent = expenses.Where(e => e.SpentOn >= m && e.SpentOn < m.AddMonths(1)).Sum(e => e.Amount);
            monthly.Add(new MonthlyFinanceDto(m.ToString("yyyy-MM"), revenue, paidSalaries, spent, revenue - paidSalaries - spent));
        }

        var gross = logs.Where(l => l.Action != PaymentAction.Refunded).Sum(l => l.Amount);
        var refunds = logs.Where(l => l.Action == PaymentAction.Refunded).Sum(l => l.Amount);
        var salaryTotal = salaries.Sum(s => s.Amount);
        var expenseTotal = expenses.Sum(e => e.Amount);

        return new FinanceSummaryDto(
            from, to, gross - refunds, refunds, salaryTotal, expenseTotal, gross - refunds - salaryTotal - expenseTotal, outstanding, monthly,
            expenses.GroupBy(e => e.Category).ToDictionary(g => g.Key, g => g.Sum(e => e.Amount)),
            await settings.CurrencyAsync(ct));
    }
}

// ---------- Reminders and read models ----------

/// <summary>
/// Marks overdue months and sends <see cref="PaymentDue"/> for months due within 3 days or
/// already overdue, at most every 3 days per payment (US-035).
/// </summary>
public interface IPaymentReminderService
{
    Task<int> RunAsync(CancellationToken ct = default);
}

internal sealed class PaymentReminderService(IFinanceDbContext db, IEventPublisher events, TimeProvider clock) : IPaymentReminderService
{
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var horizon = today.AddDays(3);
        var quietUntil = now.AddDays(-3);

        var due = await db.StudentPayments
            .Where(p => p.Status != PaymentStatus.Paid && p.Status != PaymentStatus.Cancelled && p.DueDate <= horizon)
            .Where(p => p.LastReminderOnUtc == null || p.LastReminderOnUtc < quietUntil)
            .Take(500)
            .ToListAsync(ct);

        var studentIds = due.Select(p => p.StudentUserId).Distinct().ToList();
        var guardians = await db.Guardians.Where(g => studentIds.Contains(g.StudentUserId)).ToListAsync(ct);

        foreach (var payment in due)
        {
            payment.Status = payment.ComputeStatus(today);
            payment.LastReminderOnUtc = now;
            await events.PublishAsync(new PaymentDue(
                payment.AcademyId, payment.Id, payment.StudentUserId,
                guardians.Where(g => g.StudentUserId == payment.StudentUserId).Select(g => g.ParentUserId).ToList(),
                payment.MonthNumber, payment.Remaining, payment.DueDate, payment.Status == PaymentStatus.Overdue), ct);
        }

        await db.SaveChangesAsync(ct);
        return due.Count;
    }
}

public interface IGuardianSync
{
    Task SetAsync(long academyId, long studentUserId, long? parentUserId, CancellationToken ct = default);
}

internal sealed class GuardianSync(IFinanceDbContext db) : IGuardianSync
{
    public async Task SetAsync(long academyId, long studentUserId, long? parentUserId, CancellationToken ct = default)
    {
        var existing = await db.Guardians.Where(g => g.StudentUserId == studentUserId).ToListAsync(ct);
        db.Guardians.RemoveRange(existing.Where(g => g.ParentUserId != parentUserId));
        if (parentUserId is { } parent && existing.All(g => g.ParentUserId != parent))
        {
            db.Guardians.Add(new StudentGuardian { AcademyId = academyId, StudentUserId = studentUserId, ParentUserId = parent });
        }

        await db.SaveChangesAsync(ct);
    }
}

internal sealed class SetCompensationValidator : AbstractValidator<SetCompensationRequest>
{
    public SetCompensationValidator()
    {
        RuleFor(x => x.PayType).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("The amount must be positive.");
        RuleFor(x => x.Note).MaximumLength(300);
    }
}

internal sealed class AdjustSalaryValidator : AbstractValidator<AdjustSalaryRequest>
{
    public AdjustSalaryValidator()
    {
        RuleFor(x => x.Amount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Note).NotEmpty().WithMessage("An adjustment must be documented.").MaximumLength(500);
    }
}

internal sealed class SaveExpenseValidator : AbstractValidator<SaveExpenseRequest>
{
    public SaveExpenseValidator()
    {
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Description).MaximumLength(500);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.Reference).MaximumLength(100);
    }
}
