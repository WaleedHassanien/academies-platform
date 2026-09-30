using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Domain;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.Finance.Application;
using Academies.Finance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Academies.Finance.Infrastructure.Persistence;

/// <summary>Maps only the Finance service's tables in the shared <c>academies</c> database.</summary>
public sealed class FinanceDbContext(DbContextOptions<FinanceDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser), IFinanceDbContext
{
    protected override string ServiceName => FinanceServiceInfo.Name;
    protected override bool HasPeopleDirectory => true;

    public DbSet<Person> People => Set<Person>();

    public DbSet<Compensation> Compensations => Set<Compensation>();
    public DbSet<PaymentPlan> PaymentPlans => Set<PaymentPlan>();
    public DbSet<StudentPayment> StudentPayments => Set<StudentPayment>();
    public DbSet<PaymentLog> PaymentLogs => Set<PaymentLog>();
    public DbSet<StudentGuardian> Guardians => Set<StudentGuardian>();
    public DbSet<Salary> Salaries => Set<Salary>();
    public DbSet<SalaryLog> SalaryLogs => Set<SalaryLog>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<OnlinePayment> OnlinePayments => Set<OnlinePayment>();
    public DbSet<FinanceSettings> Settings => Set<FinanceSettings>();
    public DbSet<StudentBilling> StudentBillings => Set<StudentBilling>();
    public DbSet<TeacherStudentRate> TeacherRates => Set<TeacherStudentRate>();
    public DbSet<StudentInvoiceLine> InvoiceLines => Set<StudentInvoiceLine>();
    public DbSet<TeacherPayout> Payouts => Set<TeacherPayout>();
    public DbSet<TeacherPayoutLine> PayoutLines => Set<TeacherPayoutLine>();
    public DbSet<MonthClose> MonthCloses => Set<MonthClose>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<StudentPayer> Payers => Set<StudentPayer>();
    public DbSet<AutoPayMandate> Mandates => Set<AutoPayMandate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Package>(e =>
        {
            e.ToTable("Packages");
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.MonthlyPrice).HasPrecision(12, 2);
            e.HasIndex(x => new { x.IsActive, x.Currency });
        });
        b.Entity<StudentPayer>(e =>
        {
            e.ToTable("StudentPayers");
            e.HasIndex(x => x.StudentUserId);
            e.HasIndex(x => x.PayerUserId);
        });
        b.Entity<AutoPayMandate>(e =>
        {
            e.ToTable("AutoPayMandates");
            e.Property(x => x.Provider).HasMaxLength(30);
            e.Property(x => x.Reference).HasMaxLength(80);
            e.Property(x => x.ProviderSetupId).HasMaxLength(200);
            e.Property(x => x.CustomerId).HasMaxLength(100);
            e.Property(x => x.PaymentMethodId).HasMaxLength(100);
            e.Property(x => x.CardBrand).HasMaxLength(30);
            e.Property(x => x.CardLast4).HasMaxLength(4);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => x.ProviderSetupId);
            e.HasIndex(x => new { x.StudentUserId, x.Status });
            e.HasIndex(x => x.PayerUserId);
        });

        b.Entity<Compensation>(e =>
        {
            e.ToTable("Compensations");
            e.Property(x => x.PayType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.UserId, x.EffectiveFrom });
        });
        b.Entity<PaymentPlan>(e =>
        {
            e.ToTable("PaymentPlans");
            e.Property(x => x.MonthlyAmount).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => x.StudentUserId);
        });
        b.Entity<StudentPayment>(e =>
        {
            e.ToTable("StudentPayments");
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.PaidAmount).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Ignore(x => x.Remaining);
            e.HasOne<PaymentPlan>().WithMany().HasForeignKey(x => x.PaymentPlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.StudentUserId, x.MonthNumber });
            e.HasIndex(x => new { x.Status, x.DueDate });
            e.HasIndex(x => x.PaymentPlanId);
        });
        b.Entity<PaymentLog>(e =>
        {
            e.ToTable("PaymentLogs");
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PaidByRole).HasMaxLength(30);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne<StudentPayment>().WithMany().HasForeignKey(x => x.StudentPaymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.StudentUserId, x.MonthNumber });
            e.HasIndex(x => x.ParentUserId);
            e.HasIndex(x => x.CreatedOnUtc);
        });
        b.Entity<StudentGuardian>(e =>
        {
            e.ToTable("StudentGuardians");
            e.HasIndex(x => x.StudentUserId);
            e.HasIndex(x => x.ParentUserId);
        });
        b.Entity<Salary>(e =>
        {
            e.ToTable("Salaries");
            e.Property(x => x.Role).HasMaxLength(30);
            e.Property(x => x.PayType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RatePerSession).HasPrecision(12, 2);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Ignore(x => x.MonthNumber);
            e.HasIndex(x => new { x.Year, x.Month, x.UserId });
        });
        b.Entity<SalaryLog>(e =>
        {
            e.ToTable("SalaryLogs");
            e.Property(x => x.Role).HasMaxLength(30);
            e.Property(x => x.PayType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RatePerSession).HasPrecision(12, 2);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Note).HasMaxLength(600);
            e.HasOne<Salary>().WithMany().HasForeignKey(x => x.SalaryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.UserId, x.Year, x.Month });
        });
        b.Entity<Expense>(e =>
        {
            e.ToTable("Expenses");
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.HasIndex(x => x.SpentOn);
        });
        b.Entity<OnlinePayment>(e =>
        {
            e.ToTable("OnlinePayments");
            e.Property(x => x.Provider).HasMaxLength(30);
            e.Property(x => x.Reference).HasMaxLength(80);
            e.Property(x => x.ProviderSessionId).HasMaxLength(200);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ChargedAmount).HasPrecision(12, 2);
            e.Property(x => x.ChargedCurrency).HasMaxLength(3);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.CheckoutUrl).HasMaxLength(1000);
            e.Property(x => x.FailureReason).HasMaxLength(500);
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => x.ProviderSessionId);
        });
        b.Entity<FinanceSettings>(e =>
        {
            e.ToTable("FinanceSettings");
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ExchangeRates).HasMaxLength(500);
        });
        b.Entity<StudentBilling>(e =>
        {
            e.ToTable("StudentBillings");
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PricePerSession).HasPrecision(12, 2);
            e.Property(x => x.MonthlyPrice).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.HasOne<PaymentPlan>().WithMany().HasForeignKey(x => x.PaymentPlanId).OnDelete(DeleteBehavior.Restrict);
            // One billing per student and subject (null = all subjects); enforced in the service because
            // soft-deleted rows stay in the table.
            e.HasIndex(x => new { x.StudentUserId, x.CourseId });
        });
        b.Entity<TeacherStudentRate>(e =>
        {
            e.ToTable("TeacherStudentRates");
            e.Property(x => x.RatePerSession).HasPrecision(12, 2);
            e.HasIndex(x => new { x.TeacherUserId, x.StudentUserId }).IsUnique();
        });
        b.Entity<StudentInvoiceLine>(e =>
        {
            e.ToTable("StudentInvoiceLines");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.HasOne<StudentPayment>().WithMany().HasForeignKey(x => x.StudentPaymentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.StudentPaymentId);
            // A session is billed, deducted or carried at most once.
            e.HasIndex(x => new { x.SessionId, x.Kind }).IsUnique();
        });
        b.Entity<TeacherPayout>(e =>
        {
            e.ToTable("TeacherPayouts");
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.Year, x.Month, x.TeacherUserId });
            e.HasIndex(x => new { x.TeacherUserId, x.Status });
        });
        b.Entity<TeacherPayoutLine>(e =>
        {
            e.ToTable("TeacherPayoutLines");
            e.Property(x => x.Outcome).HasMaxLength(30);
            e.Property(x => x.Rate).HasPrecision(12, 2);
            e.HasOne<TeacherPayout>().WithMany().HasForeignKey(x => x.TeacherPayoutId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SessionId).IsUnique();
            e.HasIndex(x => x.TeacherPayoutId);
        });
        b.Entity<MonthClose>(e =>
        {
            e.ToTable("MonthCloses");
            e.HasIndex(x => new { x.AcademyId, x.Year, x.Month }).IsUnique();
        });
    }
}

internal sealed class FinanceDesignTimeFactory() : DesignTimeDbContextFactoryBase<FinanceDbContext>(FinanceServiceInfo.Name)
{
    protected override FinanceDbContext Create(DbContextOptions<FinanceDbContext> options, ICurrentUser currentUser) => new(options, currentUser);
}
