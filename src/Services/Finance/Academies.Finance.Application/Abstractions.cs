using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Domain;
using Academies.Contracts.Security;
using Academies.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Academies.Finance.Application;

public interface IFinanceDbContext
{
    DbSet<Person> People { get; }
    DbSet<Compensation> Compensations { get; }
    DbSet<PaymentPlan> PaymentPlans { get; }
    DbSet<StudentPayment> StudentPayments { get; }
    DbSet<PaymentLog> PaymentLogs { get; }
    DbSet<StudentGuardian> Guardians { get; }
    DbSet<Salary> Salaries { get; }
    DbSet<SalaryLog> SalaryLogs { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<OnlinePayment> OnlinePayments { get; }
    DbSet<FinanceSettings> Settings { get; }
    DbSet<StudentBilling> StudentBillings { get; }
    DbSet<TeacherStudentRate> TeacherRates { get; }
    DbSet<StudentInvoiceLine> InvoiceLines { get; }
    DbSet<TeacherPayout> Payouts { get; }
    DbSet<TeacherPayoutLine> PayoutLines { get; }
    DbSet<MonthClose> MonthCloses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed record TeacherSessionCount(long TeacherUserId, int CompletedSessions);

/// <summary>
/// A one-to-one session and what happened to it, from Academic. <see cref="Counts"/>: the student
/// is billed and the teacher is paid. <see cref="ExcuseResolution"/> is set for excused sessions.
/// </summary>
public sealed record LedgerSession(
    long SessionId, long TeacherUserId, long StudentUserId, DateTime StartsAtUtc, int DurationMinutes, string Outcome, bool Counts,
    string? ExcuseResolution, long? MakeupOfSessionId);

/// <summary>Reads from the Academic service.</summary>
public interface IAcademicClient
{
    /// <summary>Completed sessions per teacher (fixed-salary era report).</summary>
    Task<IReadOnlyList<TeacherSessionCount>> CompletedSessionCountsAsync(long academyId, int year, int month, CancellationToken ct = default);

    Task<IReadOnlyList<LedgerSession>> LedgerAsync(
        long academyId, DateTime fromUtc, DateTime toUtc, long? teacherUserId = null, long? studentUserId = null, CancellationToken ct = default);
}

/// <summary>
/// The academy runs on Egypt time: months start and end at Cairo midnight, and the month-end
/// close happens in the last hour of the month there.
/// </summary>
public static class AcademyCalendar
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    public static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

    public static DateTime ToUtc(DateTime local) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

    /// <summary>[start, end) of a calendar month in Egypt, as UTC instants.</summary>
    public static (DateTime FromUtc, DateTime ToUtc) MonthUtc(int year, int month) =>
        (ToUtc(new DateTime(year, month, 1)), ToUtc(new DateTime(year, month, 1).AddMonths(1)));

    public static (int Year, int Month) MonthOf(DateTime utc)
    {
        var local = ToLocal(utc);
        return (local.Year, local.Month);
    }

    /// <summary>True during the last hour of the month (23:00–24:00 on the last day, Egypt time).</summary>
    public static bool IsLastHourOfMonth(DateTime utc)
    {
        var local = ToLocal(utc);
        return local.Day == DateTime.DaysInMonth(local.Year, local.Month) && local.Hour == 23;
    }
}

/// <summary><see cref="Amount"/> is in the academy currency; the gateway converts if it can't charge that currency.</summary>
public sealed record CheckoutRequest(string Reference, decimal Amount, string Currency, string Description, string? CustomerEmail);

/// <summary>Where to send the payer, and what will actually be charged.</summary>
public sealed record CheckoutSession(string ProviderSessionId, string CheckoutUrl, decimal ChargedAmount, string ChargedCurrency);

public enum CaptureOutcome
{
    Completed = 1,

    /// <summary>Accepted but not settled yet (e.g. PayPal eCheck); a later webhook finishes it.</summary>
    Pending = 2,

    Failed = 3,
}

/// <summary>Outcome of a provider capture, with our reference (sent to the provider as custom_id).</summary>
public sealed record GatewayCapture(string Reference, CaptureOutcome Outcome);

/// <summary>An online payment provider (US-039): PayPal, or Fake for development.</summary>
public interface IPaymentGateway
{
    string Name { get; }

    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default);

    /// <summary>
    /// Takes the money for an approved checkout. PayPal approves first and charges on capture.
    /// Safe to call again: an already-captured order reports its final state.
    /// </summary>
    Task<GatewayCapture> CaptureAsync(string providerSessionId, CancellationToken ct = default);
}

/// <summary>Drops cached report results for an academy whenever money data changes (US-034).</summary>
public interface IReportCache
{
    Task<T> GetOrCreateAsync<T>(long academyId, string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);
    Task InvalidateAsync(long academyId, CancellationToken ct = default);
}

internal sealed class ReportCache(ICacheService cache) : IReportCache
{
    // A per-academy version stamp is part of every report key. Changing the stamp orphans
    // all old entries at once, and they expire on their own.
    public async Task<T> GetOrCreateAsync<T>(long academyId, string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        var version = await cache.GetOrCreateAsync(VersionKey(academyId), _ => Task.FromResult(Guid.NewGuid().ToString("N")), TimeSpan.FromDays(30), ct);
        return await cache.GetOrCreateAsync($"reports:{academyId}:{version}:{key}", factory, TimeSpan.FromMinutes(30), ct);
    }

    public Task InvalidateAsync(long academyId, CancellationToken ct = default) =>
        cache.SetAsync(VersionKey(academyId), Guid.NewGuid().ToString("N"), TimeSpan.FromDays(30), ct);

    private static string VersionKey(long academyId) => $"reports:{academyId}:version";
}

/// <summary>Who may see which money records (US-030, US-033).</summary>
public sealed class FinanceAccess(IFinanceDbContext db, ICurrentUser user)
{
    public long Me => user.UserId ?? throw new UnauthorizedException("Authentication is required.");

    public long AcademyId => user.AcademyId ?? throw new ForbiddenAccessException("This action needs an academy account.");

    /// <summary>Admins and accountants see every payment record in the academy.</summary>
    public bool SeesAllPayments => user.IsSuperAdmin || user.HasPermission(Permissions.Payments.Manage);

    public bool SeesAllSalaries => user.IsSuperAdmin || user.HasPermission(Permissions.Salaries.Manage);

    /// <summary>A background job running for one academy (no user behind it).</summary>
    public bool IsSystem => user.IsAuthenticated && user.UserId is null;

    public string? PrimaryRole => user.Roles.FirstOrDefault();

    /// <summary>Students whose payments the caller may see, or null for all.</summary>
    public async Task<HashSet<long>?> VisibleStudentIdsAsync(CancellationToken ct = default)
    {
        if (SeesAllPayments)
        {
            return null;
        }

        var ids = new HashSet<long>();
        if (user.IsInRole(Roles.Parent))
        {
            ids.UnionWith(await db.Guardians.Where(g => g.ParentUserId == Me).Select(g => g.StudentUserId).ToListAsync(ct));
        }

        if (user.IsInRole(Roles.Student))
        {
            ids.Add(Me);
        }

        return ids;
    }

    public async Task EnsureCanSeeStudentAsync(long studentUserId, CancellationToken ct = default)
    {
        var visible = await VisibleStudentIdsAsync(ct);
        if (visible is not null && !visible.Contains(studentUserId))
        {
            throw new ForbiddenAccessException("You can only see your own or your children's payments.");
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
}
