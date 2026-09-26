using System.Globalization;
using System.Text.Json;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.BuildingBlocks.Infrastructure.Internal;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Security;
using Academies.Finance.Application;
using Academies.Finance.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Academies.Finance.Api.Controllers;

/// <summary>Monthly payment plans and the student's month table (US-029).</summary>
[ApiController]
[Authorize]
public sealed class PaymentPlansController(IPaymentPlanService plans) : ControllerBase
{
    [HttpPost("payment-plans")]
    [HasPermission(Permissions.Payments.Manage)]
    public async Task<ActionResult<ApiResponse<PaymentPlanDto>>> Create(CreatePaymentPlanRequest request, CancellationToken ct) =>
        Ok(ApiResponse<PaymentPlanDto>.Ok(await plans.CreateAsync(request, ct)));

    [HttpGet("payment-plans")]
    [HasPermission(Permissions.Payments.View)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PaymentPlanDto>>>> List([FromQuery] long? studentUserId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PaymentPlanDto>>.Ok(await plans.ListAsync(studentUserId, ct)));

    [HttpPost("payment-plans/{id:long}/cancel")]
    [HasPermission(Permissions.Payments.Manage)]
    public async Task<ActionResult<ApiResponse<PaymentPlanDto>>> Cancel(long id, CancellationToken ct) =>
        Ok(ApiResponse<PaymentPlanDto>.Ok(await plans.CancelAsync(id, ct)));

    [HttpGet("students/{userId:long}/payments")]
    [HasPermission(Permissions.Payments.View)]
    public async Task<ActionResult<ApiResponse<StudentPaymentsDto>>> StudentPayments(long userId, CancellationToken ct) =>
        Ok(ApiResponse<StudentPaymentsDto>.Ok(await plans.StudentPaymentsAsync(userId, ct)));
}

/// <summary>Recording money, the payment log (US-030) and online checkout (US-039).</summary>
[ApiController]
[Authorize]
public sealed class PaymentsController(IPaymentService payments, IOnlinePaymentService online) : ControllerBase
{
    [HttpPost("student-payments/{id:long}/payments")]
    [HasPermission(Permissions.Payments.Manage)]
    public async Task<ActionResult<ApiResponse<StudentPaymentDto>>> Record(long id, RecordPaymentRequest request, CancellationToken ct) =>
        Ok(ApiResponse<StudentPaymentDto>.Ok(await payments.RecordAsync(id, request, ct)));

    [HttpPost("student-payments/{id:long}/refunds")]
    [HasPermission(Permissions.Payments.Manage)]
    public async Task<ActionResult<ApiResponse<StudentPaymentDto>>> Refund(long id, RefundRequest request, CancellationToken ct) =>
        Ok(ApiResponse<StudentPaymentDto>.Ok(await payments.RefundAsync(id, request, ct)));

    /// <summary>Admins/accountants: all logs. Parents: their children's. Students: their own.</summary>
    [HttpGet("payment-logs")]
    [HasPermission(Permissions.Payments.View)]
    public async Task<ActionResult<ApiResponse<PaymentLogPageDto>>> Logs([FromQuery] PaymentLogQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PaymentLogPageDto>.Ok(await payments.LogsAsync(query, ct)));

    [HttpGet("parents/me/payment-logs")]
    [HasPermission(Permissions.Payments.View)]
    public async Task<ActionResult<ApiResponse<PaymentLogPageDto>>> MyChildrenLogs([FromQuery] PaymentLogQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PaymentLogPageDto>.Ok(await payments.LogsAsync(query, ct)));

    [HttpPost("student-payments/{id:long}/checkout")]
    [HasPermission(Permissions.Payments.View)]
    public async Task<ActionResult<ApiResponse<CheckoutDto>>> Checkout(long id, CancellationToken ct) =>
        Ok(ApiResponse<CheckoutDto>.Ok(await online.StartCheckoutAsync(id, ct)));
}

/// <summary>
/// PayPal and development callbacks (US-039). No login here. The money is always confirmed
/// with PayPal server to server: capture on return, and signature-verified webhooks.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("payments")]
public sealed class PaymentCallbacksController(
    IOnlinePaymentService online, IPaymentGateway gateway, IOptions<PaymentOptions> options, ILogger<PaymentCallbacksController> logger)
    : ControllerBase
{
    /// <summary>PayPal sends the payer here after approval (?token={orderId}); we capture and go back to the portal.</summary>
    [HttpGet("paypal/return")]
    public async Task<IActionResult> PayPalReturn([FromQuery] string token, CancellationToken ct)
    {
        GatewayCapture capture;
        try
        {
            capture = await gateway.CaptureAsync(token, ct);
            await ApplyAsync(capture, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never leave the payer on an error page; the webhook will still settle a real payment.
            logger.LogError(ex, "PayPal capture failed for order {OrderId}", token);
            return Back("failed");
        }

        return Back(capture.Outcome switch
        {
            CaptureOutcome.Completed => "success",
            CaptureOutcome.Pending => "pending",
            _ => "failed",
        });
    }

    /// <summary>The payer cancelled on PayPal. The pending order simply expires; nothing is charged.</summary>
    [HttpGet("paypal/cancel")]
    public IActionResult PayPalCancel() => Back("cancelled");

    /// <summary>
    /// Backstop for payers who close the tab before returning. Handles CHECKOUT.ORDER.APPROVED
    /// (capture now), PAYMENT.CAPTURE.COMPLETED and PAYMENT.CAPTURE.DENIED.
    /// </summary>
    [HttpPost("webhooks/paypal")]
    public async Task<IActionResult> PayPalWebhook(CancellationToken ct)
    {
        if (gateway is not PayPalPaymentGateway paypal)
        {
            return NotFound();
        }

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        if (!await paypal.VerifyWebhookAsync(name => Request.Headers[name].FirstOrDefault(), body, ct))
        {
            logger.LogWarning("Rejected PayPal webhook with an invalid signature.");
            return BadRequest(ApiResponse.Fail("Invalid signature."));
        }

        using var json = JsonDocument.Parse(body);
        var type = json.RootElement.GetProperty("event_type").GetString();
        var resource = json.RootElement.GetProperty("resource");
        var customId = resource.TryGetProperty("custom_id", out var c) ? c.GetString() : null;

        switch (type)
        {
            case "CHECKOUT.ORDER.APPROVED":
                await ApplyAsync(await gateway.CaptureAsync(resource.GetProperty("id").GetString()!, ct), ct);
                break;
            case "PAYMENT.CAPTURE.COMPLETED" when customId is not null:
                await ApplyAsync(new GatewayCapture(customId, CaptureOutcome.Completed), ct);
                break;
            case "PAYMENT.CAPTURE.DENIED" or "PAYMENT.CAPTURE.DECLINED" when customId is not null:
                await ApplyAsync(new GatewayCapture(customId, CaptureOutcome.Failed), ct);
                break;
        }

        return Ok();
    }

    /// <summary>Development only (Payments:Provider = Fake): pays at once and returns to the portal.</summary>
    [HttpGet("fake-checkout/{reference}")]
    public async Task<IActionResult> FakeCheckout(string reference, CancellationToken ct)
    {
        if (gateway is not FakePaymentGateway)
        {
            return NotFound();
        }

        await ApplyAsync(new GatewayCapture(reference, CaptureOutcome.Completed), ct);
        return Back("success");
    }

    /// <summary>Our reference is PAY-{academyId}-{paymentId}-{random}; the academy scopes the write.</summary>
    private async Task ApplyAsync(GatewayCapture capture, CancellationToken ct)
    {
        if (capture.Outcome == CaptureOutcome.Pending)
        {
            return; // a later PAYMENT.CAPTURE.COMPLETED webhook finishes it
        }

        var parts = capture.Reference.Split('-');
        if (parts.Length < 4 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var academyId))
        {
            throw new NotFoundException("Payment", capture.Reference);
        }

        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(academyId));
        await online.CompleteAsync(capture.Reference, capture.Outcome == CaptureOutcome.Completed, ct);
    }

    private RedirectResult Back(string result) => Redirect($"{options.Value.ReturnUrl}?payment={result}");
}

/// <summary>The academy's billing currency (USD or EGP).</summary>
[ApiController]
[Authorize]
[Route("settings")]
public sealed class FinanceSettingsController(IFinanceSettingsService settings) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<FinanceSettingsDto>>> Get(CancellationToken ct) =>
        Ok(ApiResponse<FinanceSettingsDto>.Ok(await settings.GetAsync(ct)));

    [HttpPut]
    [HasPermission(Permissions.Payments.Manage)]
    public async Task<ActionResult<ApiResponse<FinanceSettingsDto>>> Save(SaveFinanceSettingsRequest request, CancellationToken ct) =>
        Ok(ApiResponse<FinanceSettingsDto>.Ok(await settings.SaveAsync(request, ct)));
}

/// <summary>Pay settings (US-031), monthly salaries (US-032) and salary logs (US-033).</summary>
[ApiController]
[Authorize]
public sealed class SalariesController(ICompensationService compensations, ISalaryService salaries) : ControllerBase
{
    [HttpGet("compensations")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StaffPayDto>>>> Compensations(CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<StaffPayDto>>.Ok(await compensations.ListAsync(ct)));

    [HttpGet("compensations/{userId:long}")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CompensationDto>>>> History(long userId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<CompensationDto>>.Ok(await compensations.HistoryAsync(userId, ct)));

    [HttpPut("compensations/{userId:long}")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<CompensationDto>>> SetCompensation(long userId, SetCompensationRequest request, CancellationToken ct) =>
        Ok(ApiResponse<CompensationDto>.Ok(await compensations.SetAsync(userId, request, ct)));

    /// <summary>POST /salaries/generate?month=2026-09</summary>
    [HttpPost("salaries/generate")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<GenerateResultDto>>> Generate([FromQuery] string month, CancellationToken ct)
    {
        var (year, m) = ParseMonth(month);
        return Ok(ApiResponse<GenerateResultDto>.Ok(await salaries.GenerateAsync(year, m, ct)));
    }

    [HttpGet("salaries")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SalaryDto>>>> List([FromQuery] string month, CancellationToken ct)
    {
        var (year, m) = ParseMonth(month);
        return Ok(ApiResponse<IReadOnlyList<SalaryDto>>.Ok(await salaries.ListAsync(year, m, ct)));
    }

    [HttpPost("salaries/{id:long}/pay")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<SalaryDto>>> Pay(long id, CancellationToken ct) =>
        Ok(ApiResponse<SalaryDto>.Ok(await salaries.PayAsync(id, ct)));

    [HttpPost("salaries/{id:long}/adjust")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<SalaryDto>>> Adjust(long id, AdjustSalaryRequest request, CancellationToken ct) =>
        Ok(ApiResponse<SalaryDto>.Ok(await salaries.AdjustAsync(id, request, ct)));

    [HttpGet("salary-logs")]
    [HasPermission(Permissions.Salaries.View)]
    public async Task<ActionResult<ApiResponse<PagedResult<SalaryLogDto>>>> Logs([FromQuery] SalaryLogQuery query, CancellationToken ct) =>
        Ok(ApiResponse<PagedResult<SalaryLogDto>>.Ok(await salaries.LogsAsync(query, ct)));

    [HttpGet("me/salary-logs")]
    [HasPermission(Permissions.Salaries.View)]
    public async Task<ActionResult<ApiResponse<PagedResult<SalaryLogDto>>>> MyLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        Ok(ApiResponse<PagedResult<SalaryLogDto>>.Ok(await salaries.MyLogsAsync(page, pageSize, ct)));

    private static (int Year, int Month) ParseMonth(string month) =>
        DateOnly.TryParseExact($"{month}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? (d.Year, d.Month)
            : throw new BusinessRuleException("Month must look like 2026-09.");
}

/// <summary>Per-session billing: student prices, teacher rates, invoices and the month-end close.</summary>
[ApiController]
[Authorize]
public sealed class BillingController(IBillingSetupService setup, IStudentInvoiceService invoices, IMonthCloseService close) : ControllerBase
{
    [HttpGet("students/{userId:long}/billing")]
    [HasPermission(Permissions.Payments.View)]
    public async Task<ActionResult<ApiResponse<StudentBillingDto?>>> Get(long userId, CancellationToken ct) =>
        Ok(ApiResponse<StudentBillingDto?>.Ok(await setup.GetStudentAsync(userId, ct)));

    [HttpPut("students/{userId:long}/billing")]
    [HasPermission(Permissions.Payments.Manage)]
    public async Task<ActionResult<ApiResponse<StudentBillingDto>>> Save(long userId, SaveStudentBillingRequest request, CancellationToken ct) =>
        Ok(ApiResponse<StudentBillingDto>.Ok(await setup.SaveStudentAsync(userId, request, ct)));

    /// <summary>This month: sessions that counted, package left (prepaid) or amount so far (postpaid), balance due.</summary>
    [HttpGet("students/{userId:long}/billing/summary")]
    [HasPermission(Permissions.Payments.View)]
    public async Task<ActionResult<ApiResponse<BillingSummaryDto?>>> Summary(long userId, CancellationToken ct) =>
        Ok(ApiResponse<BillingSummaryDto?>.Ok(await setup.SummaryAsync(userId, ct)));

    [HttpGet("teacher-rates")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TeacherRateDto>>>> Rates([FromQuery] long? teacherUserId, [FromQuery] long? studentUserId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<TeacherRateDto>>.Ok(await setup.RatesAsync(teacherUserId, studentUserId, ct)));

    [HttpPut("teachers/{teacherUserId:long}/rates/{studentUserId:long}")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<TeacherRateDto>>> SetRate(long teacherUserId, long studentUserId, SetTeacherRateRequest request, CancellationToken ct) =>
        Ok(ApiResponse<TeacherRateDto>.Ok(await setup.SetRateAsync(teacherUserId, studentUserId, request, ct)));

    /// <summary>POST /invoices/generate?year=2026&amp;month=9 — postpaid for that month, prepaid for the next.</summary>
    [HttpPost("invoices/generate")]
    [HasPermission(Permissions.Payments.Manage)]
    public async Task<ActionResult<ApiResponse<InvoiceRunDto>>> GenerateInvoices([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Ok(ApiResponse<InvoiceRunDto>.Ok(await invoices.GenerateAsync(year, month, ct)));

    /// <summary>Runs the month-end close now (it also runs by itself in the month's last hour, Egypt time).</summary>
    [HttpPost("month-close")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<MonthCloseResultDto>>> Close([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Ok(ApiResponse<MonthCloseResultDto>.Ok(await close.CloseAsync(year, month, ct)));
}

/// <summary>Teacher payouts: unpaid sessions, paying now for chosen sessions, month-end payouts and confirming transfers.</summary>
[ApiController]
[Authorize]
public sealed class PayoutsController(ITeacherPayoutService payouts) : ControllerBase
{
    [HttpGet("payouts/unpaid")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UnpaidTeacherDto>>>> Unpaid([FromQuery] long? teacherUserId, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<UnpaidTeacherDto>>.Ok(await payouts.UnpaidAsync(teacherUserId, null, ct)));

    [HttpPost("payouts/pay-now")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<PayoutDto>>> PayNow(PayNowRequest request, CancellationToken ct) =>
        Ok(ApiResponse<PayoutDto>.Ok(await payouts.PayNowAsync(request, ct)));

    [HttpPost("payouts/generate")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayoutDto>>>> Generate([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PayoutDto>>.Ok(await payouts.GenerateMonthEndAsync(year, month, ct)));

    [HttpGet("payouts")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayoutDto>>>> List([FromQuery] int year, [FromQuery] int month, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<PayoutDto>>.Ok(await payouts.ListAsync(year, month, ct)));

    [HttpGet("payouts/{id:long}")]
    [HasPermission(Permissions.Salaries.View)]
    public async Task<ActionResult<ApiResponse<PayoutDto>>> Get(long id, CancellationToken ct) =>
        Ok(ApiResponse<PayoutDto>.Ok(await payouts.GetAsync(id, ct)));

    [HttpPost("payouts/{id:long}/mark-paid")]
    [HasPermission(Permissions.Salaries.Manage)]
    public async Task<ActionResult<ApiResponse<PayoutDto>>> MarkPaid(long id, MarkPayoutPaidRequest request, CancellationToken ct) =>
        Ok(ApiResponse<PayoutDto>.Ok(await payouts.MarkPaidAsync(id, request, ct)));

    /// <summary>A teacher's own earnings: unpaid sessions so far and past payouts.</summary>
    [HttpGet("me/earnings")]
    [HasPermission(Permissions.Salaries.View)]
    public async Task<ActionResult<ApiResponse<MyEarningsDto>>> Mine(CancellationToken ct) =>
        Ok(ApiResponse<MyEarningsDto>.Ok(await payouts.MineAsync(ct)));
}

/// <summary>Expenses and the profit report (US-034).</summary>
[ApiController]
[Authorize]
public sealed class ExpensesAndReportsController(IExpenseService expenses, IReportService reports) : ControllerBase
{
    [HttpGet("expenses")]
    [HasPermission(Permissions.Expenses.Manage)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ExpenseDto>>>> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Ok(ApiResponse<IReadOnlyList<ExpenseDto>>.Ok(await expenses.ListAsync(from, to, ct)));

    [HttpPost("expenses")]
    [HasPermission(Permissions.Expenses.Manage)]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Create(SaveExpenseRequest request, CancellationToken ct) =>
        Ok(ApiResponse<ExpenseDto>.Ok(await expenses.CreateAsync(request, ct)));

    [HttpPut("expenses/{id:long}")]
    [HasPermission(Permissions.Expenses.Manage)]
    public async Task<ActionResult<ApiResponse<ExpenseDto>>> Update(long id, SaveExpenseRequest request, CancellationToken ct) =>
        Ok(ApiResponse<ExpenseDto>.Ok(await expenses.UpdateAsync(id, request, ct)));

    [HttpDelete("expenses/{id:long}")]
    [HasPermission(Permissions.Expenses.Manage)]
    public async Task<ActionResult<ApiResponse>> Delete(long id, CancellationToken ct)
    {
        await expenses.DeleteAsync(id, ct);
        return Ok(ApiResponse.Ok("Expense deleted."));
    }

    [HttpGet("reports/summary")]
    [HasPermission(Permissions.Reports.View)]
    public async Task<ActionResult<ApiResponse<FinanceSummaryDto>>> Summary([FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct) =>
        Ok(ApiResponse<FinanceSummaryDto>.Ok(await reports.SummaryAsync(from, to, ct)));
}

/// <summary>Finance figures for Engagement's dashboards (US-038).</summary>
[ApiController]
[InternalApi]
[Route("internal")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalFinanceController(IReportService reports) : ControllerBase
{
    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<FinanceSummaryDto>>> Stats(
        [FromQuery] long academyId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.ForAcademy(academyId));
        return Ok(ApiResponse<FinanceSummaryDto>.Ok(await reports.SummaryAsync(from, to, ct)));
    }
}
