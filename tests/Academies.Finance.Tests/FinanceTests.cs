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
    public async Task Generate_pays_teacher_per_completed_session_and_supervisor_fixed()
    {
        await SetPayAsync();
        _academic.Counts[Teacher] = 12;

        var result = await _h.RunAsync<ISalaryService, GenerateResultDto>(s => s.GenerateAsync(2026, 9));

        result.Created.ShouldBe(2);
        var teacher = result.Salaries.Single(s => s.UserId == Teacher);
        teacher.Amount.ShouldBe(1200);
        teacher.SessionsCount.ShouldBe(12);
        teacher.RatePerSession.ShouldBe(100);
        result.Salaries.Single(s => s.UserId == Supervisor).Amount.ShouldBe(5000);
    }

    [Fact]
    public async Task Unpaid_month_is_regenerated_but_paid_month_only_changes_by_adjustment()
    {
        await SetPayAsync();
        _academic.Counts[Teacher] = 12;
        var first = await _h.RunAsync<ISalaryService, GenerateResultDto>(s => s.GenerateAsync(2026, 9));
        var teacherSalary = first.Salaries.Single(s => s.UserId == Teacher);
        var supervisorSalary = first.Salaries.Single(s => s.UserId == Supervisor);

        await _h.RunAsync<ISalaryService>(s => s.PayAsync(supervisorSalary.Id));

        _academic.Counts[Teacher] = 15;
        var second = await _h.RunAsync<ISalaryService, GenerateResultDto>(s => s.GenerateAsync(2026, 9));
        second.Regenerated.ShouldBe(1);
        second.SkippedPaid.ShouldBe(1);
        second.Salaries.Single(s => s.Id == teacherSalary.Id).Amount.ShouldBe(1500);
        second.Salaries.Single(s => s.Id == supervisorSalary.Id).Amount.ShouldBe(5000);

        var adjusted = await _h.RunAsync<ISalaryService, SalaryDto>(s => s.AdjustAsync(supervisorSalary.Id, new AdjustSalaryRequest(5200, "Overtime")));
        adjusted.Amount.ShouldBe(5200);

        var logs = await _h.RunAsync<ISalaryService, BuildingBlocks.Application.Models.PagedResult<SalaryLogDto>>(s => s.LogsAsync(new SalaryLogQuery(UserId: Supervisor)));
        logs.Items.Select(l => l.Action).ShouldBe(["Adjusted", "Paid", "Created"], ignoreOrder: true);
    }

    [Fact]
    public async Task Staff_see_only_their_own_salary_log()   // US-033
    {
        await SetPayAsync();
        _academic.Counts[Teacher] = 12;
        await _h.RunAsync<ISalaryService>(s => s.GenerateAsync(2026, 9));

        _h.User.As(Teacher, Academy, Roles.Teacher);
        var mine = await _h.RunAsync<ISalaryService, BuildingBlocks.Application.Models.PagedResult<SalaryLogDto>>(s => s.MyLogsAsync(1, 50));
        mine.Items.ShouldNotBeEmpty();
        mine.Items.ShouldAllBe(l => l.UserId == Teacher);
        mine.Items.Single().SessionsCount.ShouldBe(12);

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

    private async Task SetPayAsync()
    {
        await _h.RunAsync<ICompensationService>(s => s.SetAsync(Teacher, new SetCompensationRequest(PayType.PerSession, 100, new DateOnly(2026, 1, 1), null)));
        await _h.RunAsync<ICompensationService>(s => s.SetAsync(Supervisor, new SetCompensationRequest(PayType.MonthlyFixed, 5000, new DateOnly(2026, 1, 1), null)));
    }

    private sealed class FakeAcademic : IAcademicClient
    {
        public Dictionary<long, int> Counts { get; } = [];

        public Task<IReadOnlyList<TeacherSessionCount>> CompletedSessionCountsAsync(long academyId, int year, int month, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TeacherSessionCount>>(Counts.Select(c => new TeacherSessionCount(c.Key, c.Value)).ToList());
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
