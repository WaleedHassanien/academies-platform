using Academies.Identity.Application.Academies;
using Academies.Identity.Application.Auth;
using Academies.Identity.Application.Users;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAcademyService, AcademyService>();
        services.AddScoped<IUserService, UserService>();
        return services;
    }
}
