using Academies.BuildingBlocks.Domain;

namespace Academies.Finance.Domain;

// ---------- Currency ----------

/// <summary>
/// Currencies students can be billed in. Each student's billing has its own currency; the academy's
/// base currency is used for salaries, payouts, expenses and the totals in reports.
/// </summary>
public static class Currencies
{
    public const string USD = "USD";
    public const string EUR = "EUR";
    public const string GBP = "GBP";
    public const string SAR = "SAR";
    public const string EGP = "EGP";
    public const string Default = EGP;

    public static readonly IReadOnlyList<string> All = [USD, EUR, GBP, SAR, EGP];

    public static bool IsKnown(string? currency) => currency is not null && All.Contains(currency.ToUpperInvariant());
}

/// <summary>
/// Per-academy finance settings: the base currency (salaries, payouts, expenses, report totals) and
/// the exchange rates used to add other currencies into report totals.
/// </summary>
public sealed class FinanceSettings : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public string Currency { get; set; } = Currencies.Default;

    /// <summary>"USD=48.5;SAR=12.9": how much one unit of each currency is worth in <see cref="Currency"/>.</summary>
    public string? ExchangeRates { get; set; }

    public IReadOnlyDictionary<string, decimal> Rates() =>
        (ExchangeRates ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(pair => pair.Split('=', 2, StringSplitOptions.TrimEntries))
        .Where(p => p.Length == 2 && decimal.TryParse(p[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var r) && r > 0)
        .ToDictionary(p => p[0].ToUpperInvariant(), p => decimal.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture));

    public static string FormatRates(IReadOnlyDictionary<string, decimal> rates) =>
        string.Join(';', rates.Where(r => r.Value > 0).OrderBy(r => r.Key)
            .Select(r => $"{r.Key.ToUpperInvariant()}={r.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}"));
}

// ---------- Packages ----------

/// <summary>
/// A ready-made monthly package: sessions per month and session length at a monthly price, in one
/// currency (e.g. 8 × 30 min for 60 USD). Picking a package sets a prepaid student's billing.
/// </summary>
public sealed class Package : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public required string Name { get; set; }
    public int SessionsPerMonth { get; set; }
    public int SessionMinutes { get; set; }
    public string Currency { get; set; } = Currencies.Default;
    public decimal MonthlyPrice { get; set; }
    public bool IsActive { get; set; } = true;

    public static readonly IReadOnlyList<int> StandardSessions = [8, 12, 16, 20];
    public static readonly IReadOnlyList<int> StandardMinutes = [30, 45, 60];
}

// ---------- Pay settings (US-031) ----------

public enum PayType
{
    /// <summary>Supervisors and staff: a fixed monthly salary.</summary>
    MonthlyFixed = 1,

    /// <summary>Teachers: a rate per completed session.</summary>
    PerSession = 2,
}

/// <summary>
/// One pay setting per person per effective date. The row in force for a month is the latest
/// one whose <see cref="EffectiveFrom"/> falls on or before that month's last day.
/// </summary>
public sealed class Compensation : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long UserId { get; set; }
    public PayType PayType { get; set; }

    /// <summary>MonthlySalary for MonthlyFixed; RatePerSession for PerSession.</summary>
    public decimal Amount { get; set; }

    public DateOnly EffectiveFrom { get; set; }
    public string? Note { get; set; }
}

// ---------- Student payments (US-029, US-030) ----------

public enum PlanStatus
{
    Active = 1,
    Cancelled = 2,
}

public sealed class PaymentPlan : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public decimal MonthlyAmount { get; set; }

    /// <summary>Copied from the academy setting when the plan is created, so later changes do not relabel history.</summary>
    public string Currency { get; set; } = Currencies.Default;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Day of month each instalment is due (1..28).</summary>
    public int DueDay { get; set; } = 1;

    public PlanStatus Status { get; set; } = PlanStatus.Active;
    public string? Notes { get; set; }
}

public enum PaymentStatus
{
    Due = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Overdue = 4,
    Cancelled = 5,
}

/// <summary>One month of a plan: "Month 1", "Month 2", ... (US-029).</summary>
public sealed class StudentPayment : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long PaymentPlanId { get; set; }
    public long StudentUserId { get; set; }

    /// <summary>The subject this invoice is for, when billing is per subject.</summary>
    public long? CourseId { get; set; }

    /// <summary>Automatic card charges tried so far, and when the last one ran (see <see cref="AutoPayMandate"/>).</summary>
    public int AutoChargeAttempts { get; set; }

    public DateTime? LastAutoChargeOnUtc { get; set; }

    public int MonthNumber { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public string Currency { get; set; } = Currencies.Default;
    public PaymentStatus Status { get; set; } = PaymentStatus.Due;
    public DateTime? PaidOnUtc { get; set; }
    public DateTime? LastReminderOnUtc { get; set; }

    public decimal Remaining => Math.Max(0, Amount - PaidAmount);

    /// <summary>Status from the amounts and the due date; stored Overdue is refreshed by the reminder job.</summary>
    public PaymentStatus ComputeStatus(DateOnly today) =>
        Status == PaymentStatus.Cancelled ? PaymentStatus.Cancelled
        : PaidAmount >= Amount ? PaymentStatus.Paid
        : DueDate < today ? PaymentStatus.Overdue
        : PaidAmount > 0 ? PaymentStatus.PartiallyPaid
        : PaymentStatus.Due;
}

public enum PaymentAction
{
    Created = 1,
    Paid = 2,
    PartiallyPaid = 3,
    Refunded = 4,
    Cancelled = 5,
}

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    Card = 3,
    Online = 4,
}

/// <summary>
/// Append-only record of every money event on a student's monthly payment (US-030).
/// It is shown month by month to admins and accountants (all) and to parents (their children).
/// </summary>
public sealed class PaymentLog : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentPaymentId { get; set; }
    public long StudentUserId { get; set; }
    public long? ParentUserId { get; set; }
    public int MonthNumber { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = Currencies.Default;
    public PaymentAction Action { get; set; }
    public string? PaidByRole { get; set; }
    public PaymentMethod? Method { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
}

/// <summary>Student → guardian, copied from Academic's StudentParentChanged events.</summary>
public sealed class StudentGuardian : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public long ParentUserId { get; set; }
}

/// <summary>
/// Student → who pays (the student or a parent account), copied from Academic's StudentPayerChanged
/// events. Without a row the payer is the guardian, or else the student.
/// </summary>
public sealed class StudentPayer : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public long PayerUserId { get; set; }
}

// ---------- Salaries (US-032, US-033) ----------

public enum SalaryStatus
{
    Pending = 1,
    Paid = 2,
}

public sealed class Salary : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public PayType PayType { get; set; }
    public int? SessionsCount { get; set; }
    public decimal? RatePerSession { get; set; }
    public decimal Amount { get; set; }
    public SalaryStatus Status { get; set; } = SalaryStatus.Pending;
    public DateTime? PaidOnUtc { get; set; }
    public long? PaidByUserId { get; set; }
    public string? Note { get; set; }

    /// <summary>Months run from January 2000 = 1, so "month number" is stable across years.</summary>
    public int MonthNumber => (Year - 2000) * 12 + Month;
}

public enum SalaryAction
{
    Created = 1,
    Regenerated = 2,
    Paid = 3,
    Adjusted = 4,
}

public sealed class SalaryLog : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long SalaryId { get; set; }
    public long UserId { get; set; }
    public string Role { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public PayType PayType { get; set; }
    public int? SessionsCount { get; set; }
    public decimal? RatePerSession { get; set; }
    public decimal Amount { get; set; }
    public SalaryAction Action { get; set; }
    public long? PaidByUserId { get; set; }
    public string? Note { get; set; }
}

/// <summary>The two salary models (US-032).</summary>
public static class SalaryCalculator
{
    public static decimal Calculate(PayType payType, decimal amount, int completedSessions) => payType switch
    {
        PayType.MonthlyFixed => amount,
        PayType.PerSession => completedSessions * amount,
        _ => throw new ArgumentOutOfRangeException(nameof(payType)),
    };
}

// ---------- Per-session billing (one-to-one teaching) ----------
//
// Academic says what happened to each one-to-one session (held, absent and counted, excused...).
// Finance prices it: the student's price per session, and the teacher's rate for that student.
// Invoice lines and payout lines point at session ids, so a session is billed once and paid once.

public enum BillingMode
{
    /// <summary>Pays a monthly package (sessions per month × price) before the month starts.</summary>
    Prepaid = 1,

    /// <summary>Pays after the month ends, for the sessions that counted.</summary>
    Postpaid = 2,
}

/// <summary>
/// How a student pays for one subject (<see cref="CourseId"/>), or for all their subjects when it is
/// null. Each billing has its own currency and its own monthly invoices.
/// </summary>
public sealed class StudentBilling : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public long? CourseId { get; set; }
    public BillingMode Mode { get; set; } = BillingMode.Postpaid;

    /// <summary>Postpaid: the price of each session. Prepaid: the monthly price divided by the sessions (used for deductions).</summary>
    public decimal PricePerSession { get; set; }

    /// <summary>Prepaid package size; ignored for postpaid.</summary>
    public int SessionsPerMonth { get; set; }

    /// <summary>Prepaid: what a month costs (a package can be cheaper than sessions × price).</summary>
    public decimal MonthlyPrice { get; set; }

    /// <summary>The package this billing was set from, if any.</summary>
    public long? PackageId { get; set; }

    public int? SessionMinutes { get; set; }

    public string Currency { get; set; } = Currencies.Default;

    /// <summary>Day of month invoices fall due (1..28).</summary>
    public int DueDay { get; set; } = 1;

    /// <summary>The plan that holds this student's monthly invoices (created with the billing).</summary>
    public long PaymentPlanId { get; set; }
}

/// <summary>What a teacher earns per session with one particular student.</summary>
public sealed class TeacherStudentRate : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long TeacherUserId { get; set; }
    public long StudentUserId { get; set; }
    public decimal RatePerSession { get; set; }
}

public enum InvoiceLineKind
{
    /// <summary>A postpaid session that counted.</summary>
    Session = 1,

    /// <summary>A prepaid month's package.</summary>
    Package = 2,

    /// <summary>Excused sessions carried into this month's package at no charge.</summary>
    CarriedSessions = 3,

    /// <summary>An excused prepaid session refunded against this month.</summary>
    Deduction = 4,
}

/// <summary>What a monthly invoice (a <see cref="StudentPayment"/>) is made of.</summary>
public sealed class StudentInvoiceLine : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentPaymentId { get; set; }
    public long StudentUserId { get; set; }
    public InvoiceLineKind Kind { get; set; }

    /// <summary>The session billed, deducted or carried over; null for a package line.</summary>
    public long? SessionId { get; set; }

    public DateTime? SessionStartsAtUtc { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public enum PayoutKind
{
    /// <summary>Sent mid-month for chosen sessions.</summary>
    Interim = 1,

    /// <summary>Built automatically in the last hour of the month for everything still unpaid.</summary>
    MonthEnd = 2,
}

public enum PayoutStatus
{
    /// <summary>Calculated, waiting for the admin to confirm the transfer.</summary>
    Pending = 1,
    Paid = 2,
}

/// <summary>Money for a set of sessions sent to a teacher. Its sessions never appear as unpaid again.</summary>
public sealed class TeacherPayout : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long TeacherUserId { get; set; }
    public PayoutKind Kind { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public int SessionsCount { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = Currencies.Default;
    public PayoutStatus Status { get; set; } = PayoutStatus.Pending;
    public DateTime? PaidOnUtc { get; set; }
    public long? PaidByUserId { get; set; }
    public string? Reference { get; set; }
    public string? Note { get; set; }
}

public sealed class TeacherPayoutLine : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long TeacherPayoutId { get; set; }

    /// <summary>Unique: a session is paid once.</summary>
    public long SessionId { get; set; }

    public long StudentUserId { get; set; }
    public DateTime SessionStartsAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public decimal Rate { get; set; }
}

/// <summary>Marks that the month-end close (payouts and invoices) ran for an academy's month.</summary>
public sealed class MonthClose : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTime ClosedOnUtc { get; set; }
}

// ---------- Expenses (US-034) ----------

public sealed class Expense : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public required string Category { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public DateOnly SpentOn { get; set; }
    public string? Reference { get; set; }
}

// ---------- Online payments (US-039) ----------

public enum OnlinePaymentStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
}

public sealed class OnlinePayment : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentPaymentId { get; set; }
    public required string Provider { get; set; }

    /// <summary>Set when the charge was made automatically on a saved card.</summary>
    public long? MandateId { get; set; }

    public string? FailureReason { get; set; }


    /// <summary>Our reference, sent to the provider as custom_id and echoed back on capture and in webhooks.</summary>
    public required string Reference { get; set; }

    /// <summary>The provider's id for the checkout (the PayPal order id).</summary>
    public string? ProviderSessionId { get; set; }
    /// <summary>Amount credited to the student, in <see cref="Currency"/> (the academy currency).</summary>
    public decimal Amount { get; set; }
    public string Currency { get; set; } = Currencies.Default;

    /// <summary>What the provider actually charged, e.g. USD for an EGP month on PayPal.</summary>
    public decimal ChargedAmount { get; set; }
    public string ChargedCurrency { get; set; } = Currencies.USD;
    public OnlinePaymentStatus Status { get; set; } = OnlinePaymentStatus.Pending;
    public string? CheckoutUrl { get; set; }
    public long? InitiatedByUserId { get; set; }
    public DateTime? CompletedOnUtc { get; set; }
}

// ---------- Automatic renewal on a saved card ----------

public enum MandateStatus
{
    /// <summary>The payer was sent to the provider to save a card and hasn't finished yet.</summary>
    Pending = 1,

    Active = 2,
    Cancelled = 3,
}

/// <summary>
/// The payer's saved card for one student: each of the student's invoices is charged on it
/// automatically when it falls due (a prepaid package renews itself every month).
/// </summary>
public sealed class AutoPayMandate : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long StudentUserId { get; set; }
    public long PayerUserId { get; set; }
    public required string Provider { get; set; }

    /// <summary>Our reference for the card-saving checkout.</summary>
    public required string Reference { get; set; }

    /// <summary>The provider's id for that checkout (a Stripe Checkout Session).</summary>
    public string? ProviderSetupId { get; set; }

    public string? CustomerId { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? CardBrand { get; set; }
    public string? CardLast4 { get; set; }
    public MandateStatus Status { get; set; } = MandateStatus.Pending;
    public DateTime? ActivatedOnUtc { get; set; }
    public string? LastError { get; set; }

    /// <summary>At most this many automatic tries per invoice, a day apart.</summary>
    public const int MaxAttempts = 3;
}
