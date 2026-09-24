using Academies.BuildingBlocks.Infrastructure.Caching;
using Academies.BuildingBlocks.Infrastructure.Messaging;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.BuildingBlocks.Infrastructure.Web;
using Academies.BuildingBlocks.Infrastructure.Internal;
using Academies.Identity.Application.Abstractions;
using Academies.Identity.Infrastructure.Persistence;
using Academies.Identity.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services, IConfiguration configuration, SigningKeyStore signingKeys)
    {
        services.AddServiceDbContext<IdentityDbContext>(configuration, IdentityServiceInfo.Name);
        services.AddScoped<IIdentityDbContext>(sp => sp.GetRequiredService<IdentityDbContext>());
        services.AddPlatformCaching(configuration, IdentityServiceInfo.Name);
        services.AddPlatformMessaging<IdentityDbContext>(configuration, IdentityServiceInfo.Name);

        services.AddSingleton(signingKeys);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<IRefreshTokenStore, RedisRefreshTokenStore>();
        services.AddSingleton<IPasswordResetStore, RedisPasswordResetStore>();
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddPlatformEmail(configuration);
        services.AddPlatformAudit();
        services.AddEntitlements(configuration);
        return services;
    }
}
