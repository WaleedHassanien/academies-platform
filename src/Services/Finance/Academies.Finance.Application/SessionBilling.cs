using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Finance.Domain;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Finance.Application;

// Per-session money for one-to-one teaching:
//  - students: per subject, a monthly package (prepaid, e.g. 8 × 30 min) or a price per session
//    (postpaid, billed after the month), each in its own currency;
//  - teachers: a rate per session for each of their students, paid mid-month for chosen sessions
//    or at the automatic month-end close for everything still unpaid.
// Sessions and their outcomes come from Academic's ledger (IAcademicClient.LedgerAsync).

// ---------- DTOs ----------

public sealed record TeacherRateDto(long TeacherUserId, string? TeacherName, long StudentUserId, string? StudentName, decimal RatePerSession);

/// <summary><see cref="CourseId"/> null: the billing covers every subject without its own billing.</summary>
public sealed record StudentBillingDto(
    long Id, long StudentUserId, long? CourseId, string Mode, decimal PricePerSession, int SessionsPerMonth, decimal MonthlyPrice, int DueDay,
    string Currency, long? PackageId, int? SessionMinutes, IReadOnlyList<TeacherRateDto> Rates);

/// <summary>
/// With <see cref="PackageId"/> the billing is prepaid and takes the package's sessions, length, price and
/// currency (<see cref="MonthlyPrice"/> can override the price). Otherwise: prepaid = sessions × price
/// (or <see cref="MonthlyPrice"/>), postpaid = price per counted session. <see cref="Currency"/> defaults
/// to the academy's.
/// </summary>
public sealed record SaveStudentBillingRequest(
    BillingMode Mode, decimal PricePerSession, int SessionsPerMonth, int DueDay = 1, long? CourseId = null, long? PackageId = null,
    string? Currency = null, decimal? MonthlyPrice = null, int? SessionMinutes = null);

public sealed record SetTeacherRateRequest(decimal RatePerSession);

/// <summary>A billing's month at a glance: sessions that counted, package use (prepaid) or amount so far (postpaid).</summary>
public sealed record BillingSummaryDto(
    long BillingId, long StudentUserId, long? CourseId, string Mode, decimal PricePerSession, int SessionsPerMonth, string Currency, int Year, int Month,
    int CountedThisMonth, int CarriedIn, int PackageRemaining, decimal UnbilledAmount, decimal Outstanding);

public sealed record PackageDto(
    long Id, string Name, int SessionsPerMonth, int SessionMinutes, string Currency, decimal MonthlyPrice, decimal PricePerSession, bool IsActive);

public sealed record SavePackageRequest(string Name, int SessionsPerMonth, int SessionMinutes, string Currency, decimal MonthlyPrice, bool IsActive = true);

public sealed record UnpaidSessionDto(
    long SessionId, long StudentUserId, string? StudentName, DateTime StartsAtUtc, int DurationMinutes, string Outcome, decimal? Rate);

/// <summary>Sessions a teacher delivered that no payout covers yet. <see cref="MissingRates"/> can't be paid until a rate is set.</summary>
public sealed record UnpaidTeacherDto(
    long TeacherUserId, string? TeacherName, IReadOnlyList<UnpaidSessionDto> Sessions, decimal Total, int MissingRates, string Currency);

/// <summary><see cref="SessionIds"/> null pays every unpaid session up to now.</summary>
public sealed record PayNowRequest(long TeacherUserId, IReadOnlyList<long>? SessionIds, string? Reference, string? Note);

public sealed record MarkPayoutPaidRequest(string? Reference);

public sealed record PayoutLineDto(
    long SessionId, long StudentUserId, string? StudentName, DateTime SessionStartsAtUtc, int DurationMinutes, string Outcome, decimal Rate);

public sealed record PayoutDto(
    long Id, long TeacherUserId, string? TeacherName, string Kind, int Year, int Month, int SessionsCount, decimal Amount, string Currency,
    string Status, DateTime CreatedOnUtc, DateTime? PaidOnUtc, string? Reference, string? Note, IReadOnlyList<PayoutLineDto>? Lines);

public sealed record MyEarningsDto(UnpaidTeacherDto Unpaid, IReadOnlyList<PayoutDto> Payouts);

public sealed record InvoiceRunDto(int Created, int Updated);

public sealed record MonthCloseResultDto(int Year, int Month, int Payouts, int PaidSessions, int SessionsMissingRates, int InvoicesCreated, int InvoicesUpdated);

/// <summary>Which billing a session is charged to: the one for its subject, else the student's all-subjects billing.</summary>
internal static class BillingLookup
{
    public static StudentBilling? For(this IEnumerable<StudentBilling> billings, long studentUserId, long courseId)
    {
        var mine = billings.Where(b => b.StudentUserId == studentUserId).ToList();
        return mine.FirstOrDefault(b => b.CourseId == courseId) ?? mine.FirstOrDefault(b => b.CourseId == null);
    }

    public static bool Covers(this StudentBilling billing, IEnumerable<StudentBilling> all, LedgerSession s) =>
        all.For(s.StudentUserId, s.CourseId)?.Id == billing.Id;
}

// ---------- Packages ----------

public interface IPackageService
{
    Task<IReadOnlyList<PackageDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<PackageDto> CreateAsync(SavePackageRequest request, CancellationToken ct = default);
    Task<PackageDto> UpdateAsync(long id, SavePackageRequest request, CancellationToken ct = default);

    /// <summary>Adds the standard grid (8/12/16/20 sessions × 30/45/60 minutes) in one currency, priced per minute.</summary>
    Task<IReadOnlyList<PackageDto>> CreateStandardAsync(string currency, decimal pricePerHour, CancellationToken ct = default);
}

internal sealed class PackageService(IFinanceDbContext db, IAuditTrail audit) : IPackageService
{
    public async Task<IReadOnlyList<PackageDto>> ListAsync(bool includeInactive, CancellationToken ct = default) =>
        (await db.Packages.AsNoTracking().Where(p => includeInactive || p.IsActive)
            .OrderBy(p => p.Currency).ThenBy(p => p.SessionMinutes).ThenBy(p => p.SessionsPerMonth).ToListAsync(ct))
        .Select(ToDto).ToList();

    public async Task<PackageDto> CreateAsync(SavePackageRequest request, CancellationToken ct = default)
    {
        var package = new Package { Name = request.Name.Trim() };
        Apply(package, request);
        db.Packages.Add(package);
        await audit.RecordAsync("packages.create", nameof(Package), null, request, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(package);
    }

    public async Task<PackageDto> UpdateAsync(long id, SavePackageRequest request, CancellationToken ct = default)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException(nameof(Package), id);
        Apply(package, request);
        await audit.RecordAsync("packages.update", nameof(Package), id, request, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(package);
    }

    public async Task<IReadOnlyList<PackageDto>> CreateStandardAsync(string currency, decimal pricePerHour, CancellationToken ct = default)
    {
        currency = currency.ToUpperInvariant();
        if (!Currencies.IsKnown(currency) || pricePerHour <= 0)
        {
            throw new BusinessRuleException("Choose a currency and a positive hourly price.");
        }

        var existing = await db.Packages.Where(p => p.Currency == currency).ToListAsync(ct);
        foreach (var minutes in Package.StandardMinutes)
        {
            foreach (var sessions in Package.StandardSessions)
            {
                if (existing.Any(p => p.SessionMinutes == minutes && p.SessionsPerMonth == sessions))
                {
                    continue;
                }

                db.Packages.Add(new Package
                {
                    Name = $"{sessions} × {minutes} min", SessionsPerMonth = sessions, SessionMinutes = minutes, Currency = currency,
                    MonthlyPrice = Math.Round(pricePerHour * minutes / 60m * sessions, 2),
                });
            }
        }

        await audit.RecordAsync("packages.standard", nameof(Package), currency, new { currency, pricePerHour }, ct);
        await db.SaveChangesAsync(ct);
        return await ListAsync(false, ct);
    }

    private static void Apply(Package p, SavePackageRequest r)
    {
        p.Name = r.Name.Trim();
        p.SessionsPerMonth = r.SessionsPerMonth;
        p.SessionMinutes = r.SessionMinutes;
        p.Currency = r.Currency.ToUpperInvariant();
        p.MonthlyPrice = r.MonthlyPrice;
        p.IsActive = r.IsActive;
    }

    internal static PackageDto ToDto(Package p) => new(
        p.Id, p.Name, p.SessionsPerMonth, p.SessionMinutes, p.Currency, p.MonthlyPrice,
        p.SessionsPerMonth == 0 ? 0 : Math.Round(p.MonthlyPrice / p.SessionsPerMonth, 2), p.IsActive);
}

// ---------- Setup: prices and rates ----------

public interface IBillingSetupService
{
    Task<IReadOnlyList<StudentBillingDto>> ListStudentAsync(long studentUserId, CancellationToken ct = default);
    Task<StudentBillingDto> SaveStudentAsync(long studentUserId, SaveStudentBillingRequest request, CancellationToken ct = default);

    /// <summary>Stops billing a subject: no more invoices; existing ones stay.</summary>
    Task StopAsync(long billingId, CancellationToken ct = default);

    Task<IReadOnlyList<TeacherRateDto>> RatesAsync(long? teacherUserId, long? studentUserId, CancellationToken ct = default);
    Task<TeacherRateDto> SetRateAsync(long teacherUserId, long studentUserId, SetTeacherRateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BillingSummaryDto>> SummaryAsync(long studentUserId, CancellationToken ct = default);
}

internal sealed class BillingSetupService(
    IFinanceDbContext db, FinanceAccess access, IFinanceSettingsService settings, IStudentInvoiceService invoices, IAcademicClient academic,
    IAuditTrail audit, IReportCache reports, TimeProvider clock) : IBillingSetupService
{
    public async Task<IReadOnlyList<StudentBillingDto>> ListStudentAsync(long studentUserId, CancellationToken ct = default)
    {
        await access.EnsureCanSeeStudentAsync(studentUserId, ct);
        var billings = await db.StudentBillings.AsNoTracking().Where(b => b.StudentUserId == studentUserId).OrderBy(b => b.CourseId).ToListAsync(ct);
        var rates = await RatesAsync(null, studentUserId, ct);
        return billings.Select(b => ToDto(b, rates)).ToList();
    }

    /// <summary>
    /// Sets the price and mode of one subject (or all subjects). The first save creates the plan that
    /// holds its invoices; a prepaid billing is invoiced for the current month straight away.
    /// </summary>
    public async Task<StudentBillingDto> SaveStudentAsync(long studentUserId, SaveStudentBillingRequest request, CancellationToken ct = default)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.UserId == studentUserId, ct);
        if (person is null || !person.HasRole(Roles.Student))
        {
            throw new BusinessRuleException($"User {studentUserId} is not a student of this academy.");
        }

        var terms = await TermsAsync(request, ct);
        var billing = await db.StudentBillings.FirstOrDefaultAsync(b => b.StudentUserId == studentUserId && b.CourseId == request.CourseId, ct);
        var currency = terms.Currency ?? billing?.Currency ?? await settings.CurrencyAsync(ct);
        if (billing is null)
        {
            var plan = new PaymentPlan
            {
                StudentUserId = studentUserId, MonthlyAmount = terms.Monthly, Currency = currency, DueDay = request.DueDay,
                StartDate = DateOnly.FromDateTime(AcademyCalendar.ToLocal(clock.GetUtcNow().UtcDateTime)), EndDate = new DateOnly(2099, 12, 31),
                Notes = "Per-session billing",
            };
            db.PaymentPlans.Add(plan);
            await db.SaveChangesAsync(ct);
            billing = new StudentBilling { StudentUserId = studentUserId, CourseId = request.CourseId, PaymentPlanId = plan.Id };
            db.StudentBillings.Add(billing);
        }
        else
        {
            var plan = await db.PaymentPlans.FirstAsync(p => p.Id == billing.PaymentPlanId, ct);
            plan.MonthlyAmount = terms.Monthly;
            plan.DueDay = request.DueDay;
            plan.Currency = currency;
            plan.Status = PlanStatus.Active;
        }

        billing.Mode = terms.Mode;
        billing.PricePerSession = terms.PricePerSession;
        billing.SessionsPerMonth = terms.Mode == BillingMode.Prepaid ? terms.Sessions : 0;
        billing.MonthlyPrice = terms.Monthly;
        billing.PackageId = request.PackageId;
        billing.SessionMinutes = terms.Minutes;
        billing.Currency = currency;
        billing.DueDay = request.DueDay;
        await audit.RecordAsync("billing.student", nameof(StudentBilling), studentUserId, request, ct);
        await db.SaveChangesAsync(ct);

        if (billing.Mode == BillingMode.Prepaid)
        {
            var (year, month) = AcademyCalendar.MonthOf(clock.GetUtcNow().UtcDateTime);
            await invoices.EnsurePrepaidMonthAsync(billing, year, month, [], ct);
        }

        await reports.InvalidateAsync(access.AcademyId, ct);
        return ToDto(billing, await RatesAsync(null, studentUserId, ct));
    }

    public async Task StopAsync(long billingId, CancellationToken ct = default)
    {
        var billing = await db.StudentBillings.FirstOrDefaultAsync(b => b.Id == billingId, ct) ?? throw new NotFoundException(nameof(StudentBilling), billingId);
        var plan = await db.PaymentPlans.FirstAsync(p => p.Id == billing.PaymentPlanId, ct);
        plan.Status = PlanStatus.Cancelled;
        db.StudentBillings.Remove(billing);
        await audit.RecordAsync("billing.stop", nameof(StudentBilling), billingId, new { billing.StudentUserId, billing.CourseId }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);
    }

    private sealed record Terms(BillingMode Mode, int Sessions, decimal Monthly, decimal PricePerSession, string? Currency, int? Minutes);

    private async Task<Terms> TermsAsync(SaveStudentBillingRequest r, CancellationToken ct)
    {
        if (r.Currency is { } c && !Currencies.IsKnown(c))
        {
            throw new BusinessRuleException($"Currency must be one of {string.Join(", ", Currencies.All)}.");
        }

        if (r.PackageId is { } packageId)
        {
            var package = await db.Packages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == packageId, ct)
                          ?? throw new NotFoundException(nameof(Package), packageId);
            var monthly = r.MonthlyPrice ?? package.MonthlyPrice;
            return new Terms(BillingMode.Prepaid, package.SessionsPerMonth, monthly, Math.Round(monthly / package.SessionsPerMonth, 2),
                package.Currency, package.SessionMinutes);
        }

        if (r.Mode == BillingMode.Prepaid)
        {
            var monthly = r.MonthlyPrice ?? r.PricePerSession * r.SessionsPerMonth;
            return new Terms(BillingMode.Prepaid, r.SessionsPerMonth, monthly, Math.Round(monthly / r.SessionsPerMonth, 2),
                r.Currency?.ToUpperInvariant(), r.SessionMinutes);
        }

        return new Terms(BillingMode.Postpaid, 0, 0, r.PricePerSession, r.Currency?.ToUpperInvariant(), r.SessionMinutes);
    }

    public async Task<IReadOnlyList<TeacherRateDto>> RatesAsync(long? teacherUserId, long? studentUserId, CancellationToken ct = default)
    {
        var q = db.TeacherRates.AsNoTracking().AsQueryable();
        if (teacherUserId is { } t)
        {
            q = q.Where(r => r.TeacherUserId == t);
        }

        if (studentUserId is { } s)
        {
            q = q.Where(r => r.StudentUserId == s);
        }

        var rows = await q.ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.SelectMany(r => new[] { r.TeacherUserId, r.StudentUserId }), ct);
        return rows.Select(r => new TeacherRateDto(
                r.TeacherUserId, names.GetValueOrDefault(r.TeacherUserId), r.StudentUserId, names.GetValueOrDefault(r.StudentUserId), r.RatePerSession))
            .OrderBy(r => r.TeacherName).ThenBy(r => r.StudentName)
            .ToList();
    }

    public async Task<TeacherRateDto> SetRateAsync(long teacherUserId, long studentUserId, SetTeacherRateRequest request, CancellationToken ct = default)
    {
        var people = await db.People.Where(p => p.UserId == teacherUserId || p.UserId == studentUserId).ToListAsync(ct);
        if (!people.Any(p => p.UserId == teacherUserId && p.HasRole(Roles.Teacher)) || !people.Any(p => p.UserId == studentUserId && p.HasRole(Roles.Student)))
        {
            throw new BusinessRuleException("Choose a teacher and a student of this academy.");
        }

        var rate = await db.TeacherRates.FirstOrDefaultAsync(r => r.TeacherUserId == teacherUserId && r.StudentUserId == studentUserId, ct);
        if (rate is null)
        {
            rate = new TeacherStudentRate { TeacherUserId = teacherUserId, StudentUserId = studentUserId };
            db.TeacherRates.Add(rate);
        }

        rate.RatePerSession = request.RatePerSession;
        await audit.RecordAsync("billing.teacher_rate", nameof(TeacherStudentRate), $"{teacherUserId}:{studentUserId}", request, ct);
        await db.SaveChangesAsync(ct);
        return (await RatesAsync(teacherUserId, studentUserId, ct)).Single();
    }

    public async Task<IReadOnlyList<BillingSummaryDto>> SummaryAsync(long studentUserId, CancellationToken ct = default)
    {
        await access.EnsureCanSeeStudentAsync(studentUserId, ct);
        var billings = await db.StudentBillings.AsNoTracking().Where(b => b.StudentUserId == studentUserId).ToListAsync(ct);
        if (billings.Count == 0)
        {
            return [];
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var (year, month) = AcademyCalendar.MonthOf(now);
        var (fromUtc, toUtc) = AcademyCalendar.MonthUtc(year, month);
        var ledger = await academic.LedgerAsync(access.AcademyId, fromUtc.AddDays(-180), toUtc, studentUserId: studentUserId, ct: ct);
        var monthStart = new DateOnly(year, month, 1);
        var result = new List<BillingSummaryDto>();

        foreach (var billing in billings)
        {
            var counted = ledger.Where(l => l.Counts && billing.Covers(billings, l)).ToList();
            var countedThisMonth = counted.Count(l => l.StartsAtUtc >= fromUtc);

            var carried = await db.InvoiceLines.AsNoTracking()
                .Where(l => l.StudentUserId == studentUserId && l.Kind == InvoiceLineKind.CarriedSessions)
                .Join(db.StudentPayments.Where(p => p.PeriodStart == monthStart && p.PaymentPlanId == billing.PaymentPlanId), l => l.StudentPaymentId, p => p.Id, (l, _) => l.Quantity)
                .SumAsync(ct);

            var countedIds = counted.Select(c => c.SessionId).ToList();
            var billed = await db.InvoiceLines.AsNoTracking()
                .Where(l => l.Kind == InvoiceLineKind.Session && l.SessionId != null && countedIds.Contains(l.SessionId.Value))
                .Select(l => l.SessionId!.Value).ToListAsync(ct);
            var unbilled = billing.Mode == BillingMode.Postpaid ? counted.Count(c => !billed.Contains(c.SessionId)) * billing.PricePerSession : 0;

            var outstanding = await db.StudentPayments.AsNoTracking()
                .Where(p => p.PaymentPlanId == billing.PaymentPlanId && p.Status != PaymentStatus.Cancelled)
                .SumAsync(p => (decimal?)(p.Amount - p.PaidAmount), ct) ?? 0;

            result.Add(new BillingSummaryDto(
                billing.Id, studentUserId, billing.CourseId, billing.Mode.ToString(), billing.PricePerSession, billing.SessionsPerMonth, billing.Currency,
                year, month, countedThisMonth, carried,
                billing.Mode == BillingMode.Prepaid ? Math.Max(0, billing.SessionsPerMonth + carried - countedThisMonth) : 0,
                unbilled, Math.Max(0, outstanding)));
        }

        return result;
    }

    private static StudentBillingDto ToDto(StudentBilling b, IReadOnlyList<TeacherRateDto> rates) =>
        new(b.Id, b.StudentUserId, b.CourseId, b.Mode.ToString(), b.PricePerSession, b.SessionsPerMonth, b.MonthlyPrice, b.DueDay, b.Currency,
            b.PackageId, b.SessionMinutes, rates);
}

// ---------- Student invoices ----------

public interface IStudentInvoiceService
{
    /// <summary>
    /// Postpaid billings: an invoice for this month's counted sessions (plus any older ones not billed yet).
    /// Prepaid billings: next month's package, less deductions and plus carried sessions from this month.
    /// Safe to run again: sessions already on an invoice are skipped.
    /// </summary>
    Task<InvoiceRunDto> GenerateAsync(int year, int month, CancellationToken ct = default);

    /// <summary>Creates a prepaid billing's invoice for a month if there isn't one yet; returns whether it did.</summary>
    Task<bool> EnsurePrepaidMonthAsync(StudentBilling billing, int year, int month, IReadOnlyList<LedgerSession> previousMonth, CancellationToken ct = default);
}

internal sealed class StudentInvoiceService(
    IFinanceDbContext db, FinanceAccess access, IAcademicClient academic, IReportCache reports, IEventPublisher events, TimeProvider clock)
    : IStudentInvoiceService
{
    public async Task<InvoiceRunDto> GenerateAsync(int year, int month, CancellationToken ct = default)
    {
        var (fromUtc, toUtc) = AcademyCalendar.MonthUtc(year, month);
        var billings = await db.StudentBillings.ToListAsync(ct);
        if (billings.Count == 0)
        {
            return new InvoiceRunDto(0, 0);
        }

        var ledger = await academic.LedgerAsync(access.AcademyId, fromUtc.AddDays(-180), toUtc, ct: ct);
        var byStudent = ledger.ToLookup(l => l.StudentUserId);
        int created = 0, updated = 0;

        foreach (var billing in billings)
        {
            var sessions = byStudent[billing.StudentUserId].Where(s => billing.Covers(billings, s)).ToList();
            if (billing.Mode == BillingMode.Postpaid)
            {
                var result = await BillPostpaidAsync(billing, year, month, sessions.Where(s => s.Counts).ToList(), ct);
                created += result == true ? 1 : 0;
                updated += result == false ? 1 : 0;
            }
            else
            {
                var next = new DateTime(year, month, 1).AddMonths(1);
                var thisMonth = sessions.Where(s => s.StartsAtUtc >= fromUtc).ToList();
                created += await EnsurePrepaidMonthAsync(billing, next.Year, next.Month, thisMonth, ct) ? 1 : 0;
            }
        }

        await reports.InvalidateAsync(access.AcademyId, ct);
        return new InvoiceRunDto(created, updated);
    }

    public async Task<bool> EnsurePrepaidMonthAsync(
        StudentBilling billing, int year, int month, IReadOnlyList<LedgerSession> previousMonth, CancellationToken ct = default)
    {
        var periodStart = new DateOnly(year, month, 1);
        if (await db.StudentPayments.AnyAsync(p => p.PaymentPlanId == billing.PaymentPlanId && p.PeriodStart == periodStart && p.Status != PaymentStatus.Cancelled, ct))
        {
            return false;
        }

        var monthly = billing.MonthlyPrice > 0 ? billing.MonthlyPrice : billing.SessionsPerMonth * billing.PricePerSession;
        var lines = new List<StudentInvoiceLine>
        {
            new()
            {
                StudentUserId = billing.StudentUserId, Kind = InvoiceLineKind.Package, Quantity = billing.SessionsPerMonth,
                UnitPrice = billing.PricePerSession, Amount = monthly,
            },
        };

        // Excused sessions from the month before: carried in for free, or refunded against this invoice.
        var excused = previousMonth.Where(s => s.Outcome == "Excused").ToList();
        var handled = await HandledAsync(excused.Select(e => e.SessionId), ct);
        foreach (var s in excused.Where(s => !handled.Contains(s.SessionId)))
        {
            if (s.ExcuseResolution == "CarriedOver")
            {
                lines.Add(new StudentInvoiceLine
                {
                    StudentUserId = billing.StudentUserId, Kind = InvoiceLineKind.CarriedSessions, SessionId = s.SessionId,
                    SessionStartsAtUtc = s.StartsAtUtc, Quantity = 1, UnitPrice = 0, Amount = 0,
                });
            }
            else if (s.ExcuseResolution == "DeductedNextMonth")
            {
                lines.Add(new StudentInvoiceLine
                {
                    StudentUserId = billing.StudentUserId, Kind = InvoiceLineKind.Deduction, SessionId = s.SessionId,
                    SessionStartsAtUtc = s.StartsAtUtc, Quantity = 1, UnitPrice = -billing.PricePerSession, Amount = -billing.PricePerSession,
                });
            }
        }

        await CreateInvoiceAsync(billing, periodStart, DueDate(billing, periodStart), Math.Max(0, lines.Sum(l => l.Amount)), lines, ct);
        return true;
    }

    /// <summary>true = new invoice, false = added to this month's open invoice, null = nothing to bill.</summary>
    private async Task<bool?> BillPostpaidAsync(StudentBilling billing, int year, int month, List<LedgerSession> counted, CancellationToken ct)
    {
        var billed = await BilledAsync(counted.Select(c => c.SessionId), ct);
        var toBill = counted.Where(c => !billed.Contains(c.SessionId)).ToList();
        if (toBill.Count == 0)
        {
            return null;
        }

        var lines = toBill.Select(s => new StudentInvoiceLine
        {
            StudentUserId = billing.StudentUserId, Kind = InvoiceLineKind.Session, SessionId = s.SessionId, SessionStartsAtUtc = s.StartsAtUtc,
            Quantity = 1, UnitPrice = billing.PricePerSession, Amount = billing.PricePerSession,
        }).ToList();
        var amount = lines.Sum(l => l.Amount);

        var periodStart = new DateOnly(year, month, 1);
        var open = await db.StudentPayments.FirstOrDefaultAsync(
            p => p.PaymentPlanId == billing.PaymentPlanId && p.PeriodStart == periodStart && p.Status != PaymentStatus.Cancelled, ct);
        if (open is null)
        {
            await CreateInvoiceAsync(billing, periodStart, DueDate(billing, periodStart.AddMonths(1)), amount, lines, ct);
            return true;
        }

        // Late decisions (e.g. an absence counted after the close) join the month's invoice.
        open.Amount += amount;
        open.Status = open.ComputeStatus(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        foreach (var line in lines)
        {
            line.StudentPaymentId = open.Id;
        }

        db.InvoiceLines.AddRange(lines);
        await db.SaveChangesAsync(ct);
        return false;
    }

    /// <summary>Creates the invoice and tells the payer about it.</summary>
    private async Task CreateInvoiceAsync(
        StudentBilling billing, DateOnly periodStart, DateOnly dueDate, decimal amount, List<StudentInvoiceLine> lines, CancellationToken ct)
    {
        var monthNumber = (await db.StudentPayments.Where(p => p.StudentUserId == billing.StudentUserId).MaxAsync(p => (int?)p.MonthNumber, ct) ?? 0) + 1;
        var invoice = new StudentPayment
        {
            PaymentPlanId = billing.PaymentPlanId, StudentUserId = billing.StudentUserId, CourseId = billing.CourseId, MonthNumber = monthNumber,
            PeriodStart = periodStart, DueDate = dueDate, Amount = amount, Currency = billing.Currency,
            LastReminderOnUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.StudentPayments.Add(invoice);
        await db.SaveChangesAsync(ct);

        foreach (var line in lines)
        {
            line.StudentPaymentId = invoice.Id;
        }

        db.InvoiceLines.AddRange(lines);
        var parent = await db.Guardians.Where(g => g.StudentUserId == billing.StudentUserId).Select(g => (long?)g.ParentUserId).FirstOrDefaultAsync(ct);
        db.PaymentLogs.Add(new PaymentLog
        {
            StudentPaymentId = invoice.Id, StudentUserId = billing.StudentUserId, ParentUserId = parent, MonthNumber = monthNumber,
            Amount = amount, Currency = billing.Currency, Action = PaymentAction.Created, PaidByRole = "System",
            Note = billing.Mode == BillingMode.Prepaid ? "Monthly package" : $"{lines.Count} session(s)",
        });

        if (amount > 0)
        {
            var payer = await access.PayerOfAsync(billing.StudentUserId, ct);
            await events.PublishAsync(new PaymentDue(
                invoice.AcademyId == 0 ? access.AcademyId : invoice.AcademyId, invoice.Id, billing.StudentUserId, parent is { } p ? [p] : [],
                monthNumber, amount, dueDate, false, [payer], billing.Currency), ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private static DateOnly DueDate(StudentBilling billing, DateOnly month) =>
        new(month.Year, month.Month, Math.Min(billing.DueDay, DateTime.DaysInMonth(month.Year, month.Month)));

    private async Task<HashSet<long>> BilledAsync(IEnumerable<long> sessionIds, CancellationToken ct)
    {
        var ids = sessionIds.ToList();
        return ids.Count == 0 ? [] : (await db.InvoiceLines
            .Where(l => l.Kind == InvoiceLineKind.Session && l.SessionId != null && ids.Contains(l.SessionId.Value))
            .Select(l => l.SessionId!.Value).ToListAsync(ct)).ToHashSet();
    }

    private async Task<HashSet<long>> HandledAsync(IEnumerable<long> sessionIds, CancellationToken ct)
    {
        var ids = sessionIds.ToList();
        return ids.Count == 0 ? [] : (await db.InvoiceLines
            .Where(l => (l.Kind == InvoiceLineKind.CarriedSessions || l.Kind == InvoiceLineKind.Deduction) && l.SessionId != null && ids.Contains(l.SessionId.Value))
            .Select(l => l.SessionId!.Value).ToListAsync(ct)).ToHashSet();
    }
}

// ---------- Teacher payouts ----------

public interface ITeacherPayoutService
{
    /// <summary>Counted sessions no payout covers yet, per teacher, up to <paramref name="untilUtc"/> (default: now).</summary>
    Task<IReadOnlyList<UnpaidTeacherDto>> UnpaidAsync(long? teacherUserId, DateTime? untilUtc = null, CancellationToken ct = default);

    /// <summary>Records money sent now for chosen sessions (or all unpaid). Those sessions drop out of the month-end payout.</summary>
    Task<PayoutDto> PayNowAsync(PayNowRequest request, CancellationToken ct = default);

    /// <summary>Builds a pending payout per teacher for everything unpaid in the month; the admin confirms the transfer.</summary>
    Task<IReadOnlyList<PayoutDto>> GenerateMonthEndAsync(int year, int month, CancellationToken ct = default);

    Task<PayoutDto> MarkPaidAsync(long payoutId, MarkPayoutPaidRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PayoutDto>> ListAsync(int year, int month, CancellationToken ct = default);
    Task<PayoutDto> GetAsync(long payoutId, CancellationToken ct = default);
    Task<MyEarningsDto> MineAsync(CancellationToken ct = default);
}

internal sealed class TeacherPayoutService(
    IFinanceDbContext db, FinanceAccess access, IAcademicClient academic, IFinanceSettingsService settings, IReportCache reports,
    IAuditTrail audit, TimeProvider clock) : ITeacherPayoutService
{
    /// <summary>How far back unpaid sessions are looked for.</summary>
    private static readonly TimeSpan Lookback = TimeSpan.FromDays(180);

    public async Task<IReadOnlyList<UnpaidTeacherDto>> UnpaidAsync(long? teacherUserId, DateTime? untilUtc = null, CancellationToken ct = default)
    {
        EnsureManager();
        return await ComputeUnpaidAsync(teacherUserId, untilUtc ?? clock.GetUtcNow().UtcDateTime, ct);
    }

    public async Task<PayoutDto> PayNowAsync(PayNowRequest request, CancellationToken ct = default)
    {
        EnsureManager();
        var now = clock.GetUtcNow().UtcDateTime;
        var unpaid = (await ComputeUnpaidAsync(request.TeacherUserId, now, ct)).SingleOrDefault()
                     ?? throw new BusinessRuleException("This teacher has no unpaid sessions.");

        var chosen = request.SessionIds is null
            ? unpaid.Sessions.ToList()
            : unpaid.Sessions.Where(s => request.SessionIds.Contains(s.SessionId)).ToList();
        if (request.SessionIds is not null && chosen.Count != request.SessionIds.Distinct().Count())
        {
            throw new BusinessRuleException("Some chosen sessions are not unpaid sessions of this teacher.");
        }

        if (chosen.Count == 0)
        {
            throw new BusinessRuleException("Choose at least one session.");
        }

        if (chosen.Any(s => s.Rate is null))
        {
            throw new BusinessRuleException("Set this teacher's rate for every chosen student first.");
        }

        var (year, month) = AcademyCalendar.MonthOf(now);
        var payout = await CreatePayoutAsync(request.TeacherUserId, PayoutKind.Interim, year, month, chosen, ct);
        payout.Status = PayoutStatus.Paid;
        payout.PaidOnUtc = now;
        payout.PaidByUserId = access.Me;
        payout.Reference = request.Reference;
        payout.Note = request.Note;
        await audit.RecordAsync("payouts.pay_now", nameof(TeacherPayout), payout.Id, new { request.TeacherUserId, payout.Amount, chosen.Count }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);
        return await GetAsync(payout.Id, ct);
    }

    public async Task<IReadOnlyList<PayoutDto>> GenerateMonthEndAsync(int year, int month, CancellationToken ct = default)
    {
        EnsureManager();
        var (_, toUtc) = AcademyCalendar.MonthUtc(year, month);
        var until = new DateTime(Math.Min(toUtc.Ticks, clock.GetUtcNow().UtcDateTime.Ticks), DateTimeKind.Utc);
        var touched = new List<long>();

        foreach (var teacher in await ComputeUnpaidAsync(null, until, ct))
        {
            var payable = teacher.Sessions.Where(s => s.Rate is not null).ToList();
            if (payable.Count == 0)
            {
                continue;
            }

            var pending = await db.Payouts.FirstOrDefaultAsync(p =>
                p.TeacherUserId == teacher.TeacherUserId && p.Kind == PayoutKind.MonthEnd && p.Year == year && p.Month == month && p.Status == PayoutStatus.Pending, ct);
            if (pending is null)
            {
                pending = await CreatePayoutAsync(teacher.TeacherUserId, PayoutKind.MonthEnd, year, month, payable, ct);
            }
            else
            {
                AddLines(pending, payable);
            }

            touched.Add(pending.Id);
        }

        await audit.RecordAsync("payouts.month_end", nameof(TeacherPayout), $"{year}-{month:00}", new { payouts = touched.Count }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);

        var result = new List<PayoutDto>();
        foreach (var id in touched)
        {
            result.Add(await GetAsync(id, ct));
        }

        return result;
    }

    public async Task<PayoutDto> MarkPaidAsync(long payoutId, MarkPayoutPaidRequest request, CancellationToken ct = default)
    {
        EnsureManager();
        var payout = await db.Payouts.FirstOrDefaultAsync(p => p.Id == payoutId, ct) ?? throw new NotFoundException(nameof(TeacherPayout), payoutId);
        if (payout.Status == PayoutStatus.Paid)
        {
            throw new BusinessRuleException("This payout is already marked as transferred.");
        }

        payout.Status = PayoutStatus.Paid;
        payout.PaidOnUtc = clock.GetUtcNow().UtcDateTime;
        payout.PaidByUserId = access.Me;
        payout.Reference = request.Reference ?? payout.Reference;
        await audit.RecordAsync("payouts.mark_paid", nameof(TeacherPayout), payoutId, new { payout.Amount }, ct);
        await db.SaveChangesAsync(ct);
        await reports.InvalidateAsync(access.AcademyId, ct);
        return await GetAsync(payoutId, ct);
    }

    public async Task<IReadOnlyList<PayoutDto>> ListAsync(int year, int month, CancellationToken ct = default)
    {
        EnsureManager();
        var rows = await db.Payouts.AsNoTracking().Where(p => p.Year == year && p.Month == month).OrderByDescending(p => p.Id).ToListAsync(ct);
        var names = await db.People.NamesAsync(rows.Select(r => r.TeacherUserId), ct);
        return rows.Select(p => ToDto(p, names.GetValueOrDefault(p.TeacherUserId), null)).ToList();
    }

    /// <summary>With its sessions. Managers see any payout; a teacher only their own.</summary>
    public async Task<PayoutDto> GetAsync(long payoutId, CancellationToken ct = default)
    {
        var payout = await db.Payouts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == payoutId, ct) ?? throw new NotFoundException(nameof(TeacherPayout), payoutId);
        if (!access.SeesAllSalaries && payout.TeacherUserId != access.Me)
        {
            throw new ForbiddenAccessException("You can only see your own payouts.");
        }

        var lines = await db.PayoutLines.AsNoTracking().Where(l => l.TeacherPayoutId == payoutId).OrderBy(l => l.SessionStartsAtUtc).ToListAsync(ct);
        var names = await db.People.NamesAsync(lines.Select(l => l.StudentUserId).Append(payout.TeacherUserId), ct);
        return ToDto(payout, names.GetValueOrDefault(payout.TeacherUserId), lines.Select(l => new PayoutLineDto(
            l.SessionId, l.StudentUserId, names.GetValueOrDefault(l.StudentUserId), l.SessionStartsAtUtc, l.DurationMinutes, l.Outcome, l.Rate)).ToList());
    }

    /// <summary>A teacher's own view: what's owed so far and what has been paid.</summary>
    public async Task<MyEarningsDto> MineAsync(CancellationToken ct = default)
    {
        var me = access.Me;
        var unpaid = (await ComputeUnpaidAsync(me, clock.GetUtcNow().UtcDateTime, ct)).SingleOrDefault()
                     ?? new UnpaidTeacherDto(me, null, [], 0, 0, await settings.CurrencyAsync(ct));
        var payouts = await db.Payouts.AsNoTracking().Where(p => p.TeacherUserId == me).OrderByDescending(p => p.Id).Take(24).ToListAsync(ct);
        return new MyEarningsDto(unpaid, payouts.Select(p => ToDto(p, null, null)).ToList());
    }

    // ---- helpers ----

    private void EnsureManager()
    {
        if (!access.SeesAllSalaries && !access.IsSystem)
        {
            throw new ForbiddenAccessException("Only admins and accountants manage teacher payouts.");
        }
    }

    private async Task<List<UnpaidTeacherDto>> ComputeUnpaidAsync(long? teacherUserId, DateTime untilUtc, CancellationToken ct)
    {
        var ledger = (await academic.LedgerAsync(access.AcademyId, untilUtc - Lookback, untilUtc, teacherUserId, ct: ct))
            .Where(l => l.Counts)
            .ToList();
        if (ledger.Count == 0)
        {
            return [];
        }

        var ids = ledger.Select(l => l.SessionId).ToList();
        var paid = (await db.PayoutLines.Where(l => ids.Contains(l.SessionId)).Select(l => l.SessionId).ToListAsync(ct)).ToHashSet();
        var open = ledger.Where(l => !paid.Contains(l.SessionId)).ToList();
        if (open.Count == 0)
        {
            return [];
        }

        var teacherIds = open.Select(l => l.TeacherUserId).Distinct().ToList();
        var rates = await db.TeacherRates.AsNoTracking().Where(r => teacherIds.Contains(r.TeacherUserId))
            .ToDictionaryAsync(r => (r.TeacherUserId, r.StudentUserId), r => r.RatePerSession, ct);

        // A teacher's per-session pay setting is the fallback when no rate is set for the student.
        var fallback = (await db.Compensations.AsNoTracking()
                .Where(c => teacherIds.Contains(c.UserId) && c.PayType == PayType.PerSession)
                .ToListAsync(ct))
            .ToLookup(c => c.UserId);
        decimal? RateFor(LedgerSession s) =>
            rates.TryGetValue((s.TeacherUserId, s.StudentUserId), out var r) ? r
            : fallback[s.TeacherUserId].Where(c => c.EffectiveFrom <= DateOnly.FromDateTime(s.StartsAtUtc))
                .OrderByDescending(c => c.EffectiveFrom).ThenByDescending(c => c.Id).Select(c => (decimal?)c.Amount).FirstOrDefault();

        var names = await db.People.NamesAsync(open.SelectMany(l => new[] { l.TeacherUserId, l.StudentUserId }), ct);
        var currency = await settings.CurrencyAsync(ct);
        return open.GroupBy(l => l.TeacherUserId).Select(g =>
        {
            var sessions = g.OrderBy(s => s.StartsAtUtc).Select(s => new UnpaidSessionDto(
                s.SessionId, s.StudentUserId, names.GetValueOrDefault(s.StudentUserId), s.StartsAtUtc, s.DurationMinutes, s.Outcome, RateFor(s))).ToList();
            return new UnpaidTeacherDto(
                g.Key, names.GetValueOrDefault(g.Key), sessions, sessions.Sum(s => s.Rate ?? 0), sessions.Count(s => s.Rate is null), currency);
        }).OrderBy(t => t.TeacherName).ToList();
    }

    private async Task<TeacherPayout> CreatePayoutAsync(long teacherUserId, PayoutKind kind, int year, int month, IReadOnlyList<UnpaidSessionDto> sessions, CancellationToken ct)
    {
        var payout = new TeacherPayout { TeacherUserId = teacherUserId, Kind = kind, Year = year, Month = month, Currency = await settings.CurrencyAsync(ct) };
        db.Payouts.Add(payout);
        await db.SaveChangesAsync(ct);
        AddLines(payout, sessions);
        return payout;
    }

    private void AddLines(TeacherPayout payout, IReadOnlyList<UnpaidSessionDto> sessions)
    {
        db.PayoutLines.AddRange(sessions.Select(s => new TeacherPayoutLine
        {
            TeacherPayoutId = payout.Id, SessionId = s.SessionId, StudentUserId = s.StudentUserId, SessionStartsAtUtc = s.StartsAtUtc,
            DurationMinutes = s.DurationMinutes, Outcome = s.Outcome, Rate = s.Rate!.Value,
        }));
        payout.SessionsCount += sessions.Count;
        payout.Amount += sessions.Sum(s => s.Rate!.Value);
    }

    private static PayoutDto ToDto(TeacherPayout p, string? teacherName, IReadOnlyList<PayoutLineDto>? lines) => new(
        p.Id, p.TeacherUserId, teacherName, p.Kind.ToString(), p.Year, p.Month, p.SessionsCount, p.Amount, p.Currency, p.Status.ToString(),
        p.CreatedOnUtc, p.PaidOnUtc, p.Reference, p.Note, lines);
}

// ---------- Month-end close ----------

public interface IMonthCloseService
{
    /// <summary>Month-end payouts for every teacher plus this month's student invoices. Safe to run again.</summary>
    Task<MonthCloseResultDto> CloseAsync(int year, int month, CancellationToken ct = default);

    /// <summary>For the background job: closes the month in its last hour (Egypt time), or catches up just after.</summary>
    Task<MonthCloseResultDto?> CloseIfDueAsync(CancellationToken ct = default);
}

internal sealed class MonthCloseService(
    IFinanceDbContext db, ITeacherPayoutService payouts, IStudentInvoiceService invoices, TimeProvider clock) : IMonthCloseService
{
    public async Task<MonthCloseResultDto> CloseAsync(int year, int month, CancellationToken ct = default)
    {
        var generated = await payouts.GenerateMonthEndAsync(year, month, ct);
        var (_, toUtc) = AcademyCalendar.MonthUtc(year, month);
        var stillMissing = (await payouts.UnpaidAsync(null, new DateTime(Math.Min(toUtc.Ticks, clock.GetUtcNow().UtcDateTime.Ticks), DateTimeKind.Utc), ct))
            .Sum(t => t.MissingRates);
        var invoiced = await invoices.GenerateAsync(year, month, ct);

        if (!await db.MonthCloses.AnyAsync(c => c.Year == year && c.Month == month, ct))
        {
            db.MonthCloses.Add(new MonthClose { Year = year, Month = month, ClosedOnUtc = clock.GetUtcNow().UtcDateTime });
            await db.SaveChangesAsync(ct);
        }

        return new MonthCloseResultDto(year, month, generated.Count, generated.Sum(p => p.SessionsCount), stillMissing, invoiced.Created, invoiced.Updated);
    }

    public async Task<MonthCloseResultDto?> CloseIfDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        int year, month;
        if (AcademyCalendar.IsLastHourOfMonth(now))
        {
            (year, month) = AcademyCalendar.MonthOf(now);
        }
        else
        {
            // Missed the last hour (service down)? Catch up during the first three days of the next month.
            var local = AcademyCalendar.ToLocal(now);
            if (local.Day > 3)
            {
                return null;
            }

            var previous = new DateTime(local.Year, local.Month, 1).AddMonths(-1);
            (year, month) = (previous.Year, previous.Month);
        }

        if (await db.MonthCloses.AnyAsync(c => c.Year == year && c.Month == month, ct) || !await HasPerSessionSetupAsync(ct))
        {
            return null;
        }

        return await CloseAsync(year, month, ct);
    }

    private async Task<bool> HasPerSessionSetupAsync(CancellationToken ct) =>
        await db.StudentBillings.AnyAsync(ct) || await db.TeacherRates.AnyAsync(ct) || await db.Compensations.AnyAsync(c => c.PayType == PayType.PerSession, ct);
}

// ---------- Validators ----------

internal sealed class SaveStudentBillingValidator : AbstractValidator<SaveStudentBillingRequest>
{
    public SaveStudentBillingValidator()
    {
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.PricePerSession).GreaterThan(0).When(x => x.PackageId is null && !(x.Mode == BillingMode.Prepaid && x.MonthlyPrice > 0));
        RuleFor(x => x.SessionsPerMonth).InclusiveBetween(1, 62).When(x => x.Mode == BillingMode.Prepaid && x.PackageId is null)
            .WithMessage("A prepaid package needs between 1 and 62 sessions a month.");
        RuleFor(x => x.MonthlyPrice).GreaterThan(0).When(x => x.MonthlyPrice.HasValue);
        RuleFor(x => x.SessionMinutes).InclusiveBetween(15, 240).When(x => x.SessionMinutes.HasValue);
        RuleFor(x => x.Currency).Must(Currencies.IsKnown).When(x => x.Currency is not null)
            .WithMessage($"Currency must be one of {string.Join(", ", Currencies.All)}.");
        RuleFor(x => x.DueDay).InclusiveBetween(1, 28);
    }
}

internal sealed class SavePackageValidator : AbstractValidator<SavePackageRequest>
{
    public SavePackageValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.SessionsPerMonth).InclusiveBetween(1, 62);
        RuleFor(x => x.SessionMinutes).InclusiveBetween(15, 240);
        RuleFor(x => x.Currency).Must(Currencies.IsKnown).WithMessage($"Currency must be one of {string.Join(", ", Currencies.All)}.");
        RuleFor(x => x.MonthlyPrice).GreaterThan(0);
    }
}

internal sealed class SetTeacherRateValidator : AbstractValidator<SetTeacherRateRequest>
{
    public SetTeacherRateValidator() => RuleFor(x => x.RatePerSession).GreaterThanOrEqualTo(0);
}

internal sealed class PayNowValidator : AbstractValidator<PayNowRequest>
{
    public PayNowValidator()
    {
        RuleFor(x => x.TeacherUserId).GreaterThan(0);
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Note).MaximumLength(500);
    }
}
