using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.Finance.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddFinanceApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<FinanceAccess>();
        services.AddScoped<IReportCache, ReportCache>();
        services.AddScoped<IFinanceSettingsService, FinanceSettingsService>();
        services.AddScoped<IPaymentPlanService, PaymentPlanService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IOnlinePaymentService, OnlinePaymentService>();
        services.AddScoped<ICompensationService, CompensationService>();
        services.AddScoped<ISalaryService, SalaryService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<IPaymentReminderService, PaymentReminderService>();
        services.AddScoped<IGuardianSync, GuardianSync>();
        services.AddScoped<IStudentInvoiceService, StudentInvoiceService>();
        services.AddScoped<IBillingSetupService, BillingSetupService>();
        services.AddScoped<ITeacherPayoutService, TeacherPayoutService>();
        services.AddScoped<IMonthCloseService, MonthCloseService>();
        services.AddScoped<IPackageService, PackageService>();
        services.AddScoped<IAutoPayService, AutoPayService>();
        return services;
    }
}
