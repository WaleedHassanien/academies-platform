using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.Engagement.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddEngagementApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IEventNotifier, EventNotifier>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        return services;
    }
}
