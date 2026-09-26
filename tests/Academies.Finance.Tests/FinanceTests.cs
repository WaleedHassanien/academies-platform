using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Tests;
using Academies.Contracts.Security;
using Academies.Finance.Application;
using Academies.Finance.Domain;
using Academies.Finance.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Academies.Finance.Tests;

public sealed class FinanceTests : IAsyncLifetime
{
    private const long Academy = 1, Teacher = 10, Supervisor = 11, StudentA = 20, StudentB = 21, ParentA = 30, ParentB = 31, Accountant = 40;

    private readonly FakeAcademic _academic = new();
    private ServiceHarness<FinanceDbContext> _h = null!;

    public async ValueTask InitializeAsync()
    {
        _h = new ServiceHarness<FinanceDbContext>(s =>
        {
            s.AddScoped<IFinanceDbContext>(sp => sp.GetRequiredService<FinanceDbContext>());
            s.AddSingleton<IAcademicClient>(_academic);
            s.AddSingleton<IPaymentGateway, TestGateway>();
            s.AddFinanceApplication();
        });

        await _h.SeedAsync(db =>
        {
            db.People.AddRange(
                PeopleSeed.Person(Academy, Teacher, "Teacher T", Roles.Teacher),
                PeopleSeed.Person(Academy, Supervisor, "Supervisor S", Roles.Supervisor),
                PeopleSeed.Person(Academy, StudentA, "Student A", Roles.Student),
                PeopleSeed.Person(Academy, StudentB, "Student B", Roles.Student),
                PeopleSeed.Person(Academy, ParentA, "Parent A", Roles.Parent),
                PeopleSeed.Person(Academy, ParentB, "Parent B", Roles.Parent),
                PeopleSeed.Person(Academy, Accountant, "Accountant", Roles.Accountant));
            db.Guardians.AddRange(
                new StudentGuardian { AcademyId = Academy, StudentUserId = StudentA, ParentUserId = ParentA },
                new StudentGuardian { AcademyId = Academy, StudentUserId = StudentB, ParentUserId = ParentB });
            return Task.CompletedTask;
        });

        _h.User.As(Accountant, Academy, Roles.Accountant);
    }

    public async ValueTask DisposeAsync() => await _h.DisposeAsync();

    // ---------- US-032: the two salary models ----------

    [Fact]
    public void Salary_models_per_session_and_fixed()
    {
        SalaryCalculator.Calculate(PayType.PerSession, 100, 12).ShouldBe(1200);
        SalaryCalculator.Calculate(PayType.MonthlyFixed, 5000, 0).ShouldBe(5000);
        SalaryCalculator.Calculate(PayType.MonthlyFixed, 5000, 30).ShouldBe(5000);
    }

    [Fact]
    public async Task Generate_builds_fixed_salaries_only_teachers_go_through_payouts()
    {
        await SetPayAsync();

        var result = await _h.RunAsync<ISalaryService, GenerateResultDto>(s => s.GenerateAsync(2026, 9));

        result.Created.ShouldBe(1);
        result.Salaries.Single().UserId.ShouldBe(Supervisor);
        result.Salaries.Single().Amount.ShouldBe(5000);
    }

    [Fact]
    public async Task Unpaid_month_is_regenerated_but_paid_month_only_changes_by_adjustment()
    {
        await SetPayAsync();
        var first = await _h.RunAsync<ISalaryService, GenerateResultDto>(s => s.GenerateAsync(2026, 9));
        var supervisorSalary = first.Salaries.Single(s => s.UserId == Supervisor);

        // Unpaid: a new pay setting is picked up on the next run.
        await _h.RunAsync<ICompensationService>(s => s.SetAsync(Supervisor, new SetCompensationRequest(PayType.MonthlyFixed, 5500, new DateOnly(2026, 9, 1), null)));
        var second = await _h.RunAsync<ISalaryService, GenerateResultDto>(s => s.GenerateAsync(2026, 9));
        second.Regenerated.ShouldBe(1);
        second.Salaries.Single().Amount.ShouldBe(5500);

        await _h.RunAsync<ISalaryService>(s => s.PayAsync(supervisorSalary.Id));
        await _h.RunAsync<ICompensationService>(s => s.SetAsync(Supervisor, new SetCompensationRequest(PayType.MonthlyFixed, 6000, new DateOnly(2026, 9, 2), null)));
        var third = await _h.RunAsync<ISalaryService, GenerateResultDto>(s => s.GenerateAsync(2026, 9));
        third.SkippedPaid.ShouldBe(1);
        third.Salaries.Single().Amount.ShouldBe(5500);

        var adjusted = await _h.RunAsync<ISalaryService, SalaryDto>(s => s.AdjustAsync(supervisorSalary.Id, new AdjustSalaryRequest(5700, "Overtime")));
        adjusted.Amount.ShouldBe(5700);

        var logs = await _h.RunAsync<ISalaryService, BuildingBlocks.Application.Models.PagedResult<SalaryLogDto>>(s => s.LogsAsync(new SalaryLogQuery(UserId: Supervisor)));
        logs.Items.Select(l => l.Action).ShouldBe(["Adjusted", "Paid", "Regenerated", "Created"], ignoreOrder: true);
    }

    [Fact]
    public async Task Staff_see_only_their_own_salary_log()   // US-033
    {
        await SetPayAsync();
        await _h.RunAsync<ISalaryService>(s => s.GenerateAsync(2026, 9));

        _h.User.As(Supervisor, Academy, Roles.Supervisor);
        var mine = await _h.RunAsync<ISalaryService, BuildingBlocks.Application.Models.PagedResult<SalaryLogDto>>(s => s.MyLogsAsync(1, 50));
        mine.Items.ShouldNotBeEmpty();
        mine.Items.ShouldAllBe(l => l.UserId == Supervisor);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            _h.RunAsync<ISalaryService>(s => s.LogsAsync(new SalaryLogQuery())));
    }

    [Fact]
    public async Task Only_teachers_can_be_paid_per_session()   // US-031
    {
        await Should.ThrowAsync<BusinessRuleException>(() => _h.RunAsync<ICompensationService>(s =>
            s.SetAsync(Supervisor, new SetCompensationRequest(PayType.PerSession, 100, new DateOnly(2026, 1, 1), null))));
    }

    // ---------- US-029 / US-030: monthly payments and their log ----------

    [Fact]
    public async Task Plan_creates_one_payment_per_month_and_payments_update_status()
    {
        await _h.RunAsync<IPaymentPlanService>(s => s.CreateAsync(new CreatePaymentPlanRequest(StudentA, 500, new DateOnly(2026, 10, 1), 3, 5)));

        var table = await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentA));
        table.Months.Select(m => m.MonthNumber).ShouldBe([1, 2, 3]);
        table.Months.Select(m => m.DueDate).ShouldBe([new DateOnly(2026, 10, 5), new DateOnly(2026, 11, 5), new DateOnly(2026, 12, 5)]);
        table.Months.ShouldAllBe(m => m.Status == "Due");

        var first = table.Months[0].Id;
        (await _h.RunAsync<IPaymentService, StudentPaymentDto>(s => s.RecordAsync(first, new RecordPaymentRequest(200, PaymentMethod.Cash, null, null))))
            .Status.ShouldBe("PartiallyPaid");
        (await _h.RunAsync<IPaymentService, StudentPaymentDto>(s => s.RecordAsync(first, new RecordPaymentRequest(300, PaymentMethod.BankTransfer, "TRX-1", null))))
            .Status.ShouldBe("Paid");
        await Should.ThrowAsync<BusinessRuleException>(() =>
            _h.RunAsync<IPaymentService>(s => s.RecordAsync(first, new RecordPaymentRequest(1, PaymentMethod.Cash, null, null))));

        var logs = await _h.RunAsync<IPaymentService, PaymentLogPageDto>(s => s.LogsAsync(new PaymentLogQuery(StudentUserId: StudentA)));
        logs.Logs.Items.Count(l => l.Action == "Created").ShouldBe(3);
        logs.TotalPaid.ShouldBe(500);
        logs.Logs.Items.Where(l => l.Action != "Created").ShouldAllBe(l => l.ParentUserId == ParentA);
    }

    [Fact]
    public async Task Parent_sees_only_their_childrens_payment_log()   // US-030
    {
        await _h.RunAsync<IPaymentPlanService>(s => s.CreateAsync(new CreatePaymentPlanRequest(StudentA, 500, new DateOnly(2026, 10, 1), 2)));
        await _h.RunAsync<IPaymentPlanService>(s => s.CreateAsync(new CreatePaymentPlanRequest(StudentB, 700, new DateOnly(2026, 10, 1), 2)));

        var all = await _h.RunAsync<IPaymentService, PaymentLogPageDto>(s => s.LogsAsync(new PaymentLogQuery()));
        all.Logs.Items.Select(l => l.StudentUserId).Distinct().ShouldBe([StudentA, StudentB], ignoreOrder: true);

        _h.User.As(ParentA, Academy, Roles.Parent);
        var mine = await _h.RunAsync<IPaymentService, PaymentLogPageDto>(s => s.LogsAsync(new PaymentLogQuery()));
        mine.Logs.Items.ShouldNotBeEmpty();
        mine.Logs.Items.ShouldAllBe(l => l.StudentUserId == StudentA);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            _h.RunAsync<IPaymentService>(s => s.LogsAsync(new PaymentLogQuery(StudentUserId: StudentB))));
        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            _h.RunAsync<IPaymentPlanService>(s => s.StudentPaymentsAsync(StudentB)));
    }

    [Fact]
    public async Task Report_nets_revenue_against_salaries_and_expenses()   // US-034
    {
        await _h.RunAsync<IPaymentPlanService>(s => s.CreateAsync(new CreatePaymentPlanRequest(StudentA, 1000, new DateOnly(2026, 9, 1), 1)));
        var payment = (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentA))).Months[0];
        await _h.RunAsync<IPaymentService>(s => s.RecordAsync(payment.Id, new RecordPaymentRequest(1000, PaymentMethod.Cash, null, null)));
        await _h.RunAsync<IExpenseService>(s => s.CreateAsync(new SaveExpenseRequest("Rent", null, 300, new DateOnly(2026, 9, 10), null)));

        var today = new DateOnly(2026, 9, 30);
        var report = await _h.RunAsync<IReportService, FinanceSummaryDto>(s => s.SummaryAsync(new DateOnly(2026, 9, 1), today));
        report.Revenue.ShouldBe(1000);
        report.Expenses.ShouldBe(300);
        report.Net.ShouldBe(700);

        // A new expense invalidates the cached report.
        await _h.RunAsync<IExpenseService>(s => s.CreateAsync(new SaveExpenseRequest("Utilities", null, 100, new DateOnly(2026, 9, 11), null)));
        (await _h.RunAsync<IReportService, FinanceSummaryDto>(s => s.SummaryAsync(new DateOnly(2026, 9, 1), today))).Net.ShouldBe(600);
    }

    [Fact]
    public async Task Plans_use_the_academy_currency_and_keep_it()
    {
        await _h.RunAsync<IPaymentPlanService>(s => s.CreateAsync(new CreatePaymentPlanRequest(StudentA, 500, new DateOnly(2026, 10, 1), 1)));
        (await _h.RunAsync<IFinanceSettingsService, FinanceSettingsDto>(s => s.SaveAsync(new SaveFinanceSettingsRequest("usd")))).Currency.ShouldBe("USD");
        await _h.RunAsync<IPaymentPlanService>(s => s.CreateAsync(new CreatePaymentPlanRequest(StudentB, 40, new DateOnly(2026, 10, 1), 1)));

        (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentA))).Currency.ShouldBe(Currencies.EGP);
        (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentB))).Months.Single().Currency.ShouldBe(Currencies.USD);
        await Should.ThrowAsync<BusinessRuleException>(() =>
            _h.RunAsync<IFinanceSettingsService>(s => s.SaveAsync(new SaveFinanceSettingsRequest("SAR"))));
    }

    [Fact]
    public async Task Online_checkout_records_charged_amount_and_completes_once()   // US-039
    {
        await _h.RunAsync<IPaymentPlanService>(s => s.CreateAsync(new CreatePaymentPlanRequest(StudentA, 1000, new DateOnly(2026, 10, 1), 1)));
        var month = (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentA))).Months.Single();

        _h.User.As(ParentA, Academy, Roles.Parent);
        var checkout = await _h.RunAsync<IOnlinePaymentService, CheckoutDto>(s => s.StartCheckoutAsync(month.Id));
        checkout.Currency.ShouldBe(Currencies.EGP);
        checkout.ChargedAmount.ShouldBe(1000);

        _h.User.As(0, Academy);
        await _h.RunAsync<IOnlinePaymentService>(s => s.CompleteAsync(checkout.Reference, succeeded: true));
        await _h.RunAsync<IOnlinePaymentService>(s => s.CompleteAsync(checkout.Reference, succeeded: true)); // webhook retry

        _h.User.As(Accountant, Academy, Roles.Accountant);
        var paid = (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentA))).Months.Single();
        paid.Status.ShouldBe("Paid");
        paid.PaidAmount.ShouldBe(1000);
    }

    // ---------- Per-session billing and teacher payouts ----------

    private const long StudentC = 22;

    private static DateTime Sep(int day, int hour = 16) => new(2026, 9, day, hour, 0, 0, DateTimeKind.Utc);

    private void Session(long id, long student, DateTime at, string outcome = "Held", string? excuse = null) =>
        _academic.Ledger.Add(new LedgerSession(id, Teacher, student, at, 30, outcome, outcome is "Held" or "AbsentCounted", excuse, null));

    [Fact]
    public async Task Sessions_paid_mid_month_are_left_out_of_the_month_end_payout()
    {
        await _h.SeedAsync(db =>
        {
            db.People.Add(PeopleSeed.Person(Academy, StudentC, "Student C", Roles.Student));
            return Task.CompletedTask;
        });
        await _h.RunAsync<IBillingSetupService>(s => s.SetRateAsync(Teacher, StudentA, new SetTeacherRateRequest(50)));
        await _h.RunAsync<IBillingSetupService>(s => s.SetRateAsync(Teacher, StudentB, new SetTeacherRateRequest(70)));
        Session(1, StudentA, Sep(2));
        Session(2, StudentA, Sep(5));
        Session(3, StudentA, Sep(10), "AbsentCounted");
        Session(4, StudentB, Sep(12));
        Session(5, StudentB, Sep(13), "AbsentNotCounted");
        Session(6, StudentC, Sep(14));   // no rate yet

        var unpaid = (await _h.RunAsync<ITeacherPayoutService, IReadOnlyList<UnpaidTeacherDto>>(s => s.UnpaidAsync(Teacher))).Single();
        unpaid.Sessions.Select(s => s.SessionId).ShouldBe([1, 2, 3, 4, 6]);
        unpaid.Total.ShouldBe(220);
        unpaid.MissingRates.ShouldBe(1);

        // Day 15: the admin sends money for two sessions.
        await Should.ThrowAsync<BusinessRuleException>(() => _h.RunAsync<ITeacherPayoutService>(s => s.PayNowAsync(new PayNowRequest(Teacher, [1, 6], null, null))));
        var interim = await _h.RunAsync<ITeacherPayoutService, PayoutDto>(s => s.PayNowAsync(new PayNowRequest(Teacher, [1, 2], "TRX-15", null)));
        interim.Status.ShouldBe("Paid");
        interim.Amount.ShouldBe(100);

        // Last hour of the month in Egypt (UTC+3 in September): the close builds a pending payout for the rest.
        _h.Clock.Now = new DateTimeOffset(2026, 9, 30, 20, 30, 0, TimeSpan.Zero);
        AcademyCalendar.IsLastHourOfMonth(_h.Clock.Now.UtcDateTime).ShouldBeTrue();
        var close = await _h.RunAsync<IMonthCloseService, MonthCloseResultDto>(s => s.CloseAsync(2026, 9));
        close.Payouts.ShouldBe(1);
        close.SessionsMissingRates.ShouldBe(1);

        var monthEnd = (await _h.RunAsync<ITeacherPayoutService, IReadOnlyList<PayoutDto>>(s => s.ListAsync(2026, 9))).Single(p => p.Kind == "MonthEnd");
        monthEnd.Status.ShouldBe("Pending");
        monthEnd.Amount.ShouldBe(120);
        (await _h.RunAsync<ITeacherPayoutService, PayoutDto>(s => s.GetAsync(monthEnd.Id))).Lines!.Select(l => l.SessionId).ShouldBe([3, 4]);

        // Once the rate is set, the forgotten session is the only thing left.
        await _h.RunAsync<IBillingSetupService>(s => s.SetRateAsync(Teacher, StudentC, new SetTeacherRateRequest(60)));
        var left = (await _h.RunAsync<ITeacherPayoutService, IReadOnlyList<UnpaidTeacherDto>>(s => s.UnpaidAsync(Teacher))).Single();
        left.Sessions.Select(s => s.SessionId).ShouldBe([6]);

        await _h.RunAsync<ITeacherPayoutService>(s => s.MarkPaidAsync(monthEnd.Id, new MarkPayoutPaidRequest("TRX-30")));
        var report = await _h.RunAsync<IReportService, FinanceSummaryDto>(s => s.SummaryAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        report.Salaries.ShouldBe(220);

        // The teacher sees their own payouts, not the admin screens.
        _h.User.As(Teacher, Academy, Roles.Teacher);
        (await _h.RunAsync<ITeacherPayoutService, MyEarningsDto>(s => s.MineAsync())).Payouts.Count.ShouldBe(2);
        await Should.ThrowAsync<ForbiddenAccessException>(() => _h.RunAsync<ITeacherPayoutService>(s => s.ListAsync(2026, 9)));
    }

    [Fact]
    public async Task Postpaid_students_are_invoiced_for_counted_sessions_once()
    {
        await _h.RunAsync<IBillingSetupService>(s => s.SaveStudentAsync(StudentA, new SaveStudentBillingRequest(BillingMode.Postpaid, 100, 0, 5)));
        Session(1, StudentA, Sep(2));
        Session(2, StudentA, Sep(9));
        Session(3, StudentA, Sep(16), "AbsentNotCounted");
        Session(4, StudentA, Sep(23), "AbsentCounted");

        (await _h.RunAsync<IStudentInvoiceService, InvoiceRunDto>(s => s.GenerateAsync(2026, 9))).Created.ShouldBe(1);
        (await _h.RunAsync<IStudentInvoiceService, InvoiceRunDto>(s => s.GenerateAsync(2026, 9))).ShouldBe(new InvoiceRunDto(0, 0));

        var invoice = (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentA))).Months.Single();
        invoice.Amount.ShouldBe(300);
        invoice.DueDate.ShouldBe(new DateOnly(2026, 10, 5));

        // A session held later in the month joins the same invoice.
        Session(5, StudentA, Sep(28));
        (await _h.RunAsync<IStudentInvoiceService, InvoiceRunDto>(s => s.GenerateAsync(2026, 9))).Updated.ShouldBe(1);
        (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentA))).Months.Single().Amount.ShouldBe(400);
    }

    [Fact]
    public async Task Prepaid_package_is_invoiced_ahead_with_deductions_and_carried_sessions()
    {
        // Saving a prepaid student bills the current month's package at once.
        await _h.RunAsync<IBillingSetupService>(s => s.SaveStudentAsync(StudentB, new SaveStudentBillingRequest(BillingMode.Prepaid, 80, 8)));
        (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentB))).Months.Single().Amount.ShouldBe(640);

        Session(1, StudentB, Sep(3));
        Session(2, StudentB, Sep(7));
        Session(3, StudentB, Sep(10), "Excused", "DeductedNextMonth");
        Session(4, StudentB, Sep(14), "Excused", "CarriedOver");
        Session(5, StudentB, Sep(20), "Excused", "NotCounted");

        var summary = await _h.RunAsync<IBillingSetupService, BillingSummaryDto?>(s => s.SummaryAsync(StudentB));
        summary!.CountedThisMonth.ShouldBe(2);
        summary.PackageRemaining.ShouldBe(6);

        await _h.RunAsync<IStudentInvoiceService>(s => s.GenerateAsync(2026, 9));
        var october = (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentB))).Months
            .Single(m => m.PeriodStart == new DateOnly(2026, 10, 1));
        october.Amount.ShouldBe(560);   // 8 × 80, less one deducted session

        // Running it again changes nothing.
        await _h.RunAsync<IStudentInvoiceService>(s => s.GenerateAsync(2026, 9));
        (await _h.RunAsync<IPaymentPlanService, StudentPaymentsDto>(s => s.StudentPaymentsAsync(StudentB))).Months.Count.ShouldBe(2);

        // In October the carried session tops up the package.
        _h.Clock.Now = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
        var oct = await _h.RunAsync<IBillingSetupService, BillingSummaryDto?>(s => s.SummaryAsync(StudentB));
        oct!.CarriedIn.ShouldBe(1);
        oct.PackageRemaining.ShouldBe(9);
    }

    private async Task SetPayAsync()
    {
        await _h.RunAsync<ICompensationService>(s => s.SetAsync(Teacher, new SetCompensationRequest(PayType.PerSession, 100, new DateOnly(2026, 1, 1), null)));
        await _h.RunAsync<ICompensationService>(s => s.SetAsync(Supervisor, new SetCompensationRequest(PayType.MonthlyFixed, 5000, new DateOnly(2026, 1, 1), null)));
    }

    private sealed class FakeAcademic : IAcademicClient
    {
        public Dictionary<long, int> Counts { get; } = [];
        public List<LedgerSession> Ledger { get; } = [];

        public Task<IReadOnlyList<TeacherSessionCount>> CompletedSessionCountsAsync(long academyId, int year, int month, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TeacherSessionCount>>(Counts.Select(c => new TeacherSessionCount(c.Key, c.Value)).ToList());

        public Task<IReadOnlyList<LedgerSession>> LedgerAsync(
            long academyId, DateTime fromUtc, DateTime toUtc, long? teacherUserId = null, long? studentUserId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LedgerSession>>(Ledger
                .Where(l => l.StartsAtUtc >= fromUtc && l.StartsAtUtc < toUtc)
                .Where(l => teacherUserId == null || l.TeacherUserId == teacherUserId)
                .Where(l => studentUserId == null || l.StudentUserId == studentUserId)
                .ToList());
    }

    private sealed class TestGateway : IPaymentGateway
    {
        public string Name => "Test";

        public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CheckoutSession(request.Reference, $"https://pay.test/{request.Reference}", request.Amount, request.Currency));

        public Task<GatewayCapture> CaptureAsync(string providerSessionId, CancellationToken ct = default) =>
            Task.FromResult(new GatewayCapture(providerSessionId, CaptureOutcome.Completed));
    }
}
