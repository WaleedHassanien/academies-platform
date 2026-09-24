using System.Text.Json;
using Academies.BuildingBlocks.Application.Models;
using Academies.Contracts.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Academies.BuildingBlocks.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "academies-identity";
    public string Audience { get; set; } = "academies-api";

    /// <summary>
    /// Base URL of the Identity service. JwtBearer reads <c>/.well-known/openid-configuration</c>
    /// there and follows <c>jwks_uri</c> to get the RS256 public key.
    /// </summary>
    public string Authority { get; set; } = "http://localhost:5101";

    public bool RequireHttpsMetadata { get; set; }
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>PEM file holding the RSA private key (Identity only).</summary>
    public string SigningKeyPath { get; set; } = "keys/signing.pem";
}

public static class AuthenticationExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// JWT bearer auth plus permission policies. Identity passes its own <paramref name="localSigningKey"/>,
    /// so it never calls itself over HTTP. Every other service discovers the key from <see cref="JwtOptions.Authority"/>.
    /// </summary>
    public static IServiceCollection AddPlatformAuthentication(
        this IServiceCollection services, IConfiguration configuration, SecurityKey? localSigningKey = null)
    {
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                if (localSigningKey is null)
                {
                    options.Authority = jwt.Authority;
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = localSigningKey,
                    NameClaimType = AppClaims.Name,
                    RoleClaimType = AppClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await WriteAsync(context.Response, StatusCodes.Status401Unauthorized, "Authentication is required.");
                    },
                    OnForbidden = context =>
                        WriteAsync(context.Response, StatusCodes.Status403Forbidden, "You do not have permission to perform this action."),
                };
            });

        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        services.AddAuthorization();
        return services;
    }

    private static Task WriteAsync(HttpResponse response, int status, string message)
    {
        if (response.HasStarted)
        {
            return Task.CompletedTask;
        }

        response.StatusCode = status;
        response.ContentType = "application/json";
        return response.WriteAsync(JsonSerializer.Serialize(ApiResponse.Fail(message), Json));
    }
}
