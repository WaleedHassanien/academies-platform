using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.Subscription.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddSubscriptionApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<TrialProvisioner>();
        services.AddScoped<IEntitlementsService, EntitlementsService>();
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<IAcademySubscriptionService, AcademySubscriptionService>();
        return services;
    }
}
