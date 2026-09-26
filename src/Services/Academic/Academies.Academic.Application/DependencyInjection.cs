using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.Academic.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAcademicApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<AccessGuard>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IWorkScheduleService, WorkScheduleService>();
        services.AddScoped<IRelationshipService, RelationshipService>();
        services.AddScoped<ICourseService, CourseService>();
        services.AddScoped<SessionService>();
        services.AddScoped<ISessionService>(sp => sp.GetRequiredService<SessionService>());
        services.AddScoped<ISessionOutcomeService>(sp => sp.GetRequiredService<SessionService>());
        services.AddScoped<IGamificationService, GamificationService>();
        services.AddScoped<IAssignmentService, AssignmentService>();
        services.AddScoped<ICertificateService, CertificateService>();
        services.AddScoped<IOverviewService, OverviewService>();
        services.AddScoped<IProfileSync, ProfileSync>();
        services.AddScoped<ISessionReminderService, SessionReminderService>();
        return services;
    }
}
