using Academies.BuildingBlocks.Domain;

namespace Academies.Finance.Domain;

// ---------- Currency ----------

/// <summary>Currencies an academy can bill in.</summary>
public static class Currencies
{
    public const string USD = "USD";
    public const string EGP = "EGP";
    public const string Default = EGP;

    public static readonly IReadOnlyList<string> All = [USD, EGP];
}

/// <summary>Per-academy finance settings: the billing currency for plans, payments, salaries and reports.</summary>
public sealed class FinanceSettings : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public string Currency { get; set; } = Currencies.Default;
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
