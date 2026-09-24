using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Models;
using Academies.Contracts.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.BuildingBlocks.Infrastructure.Internal;

/// <summary>
/// Marks a controller as service-to-service only. Callers must send the shared
/// <c>Internal:ApiKey</c> in <see cref="HeaderName"/>. The gateway also refuses any path
/// containing <c>/internal/</c>, so these routes are never public.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalApiAttribute : Attribute, IAllowAnonymous, IAuthorizationFilter
{
    public const string HeaderName = "X-Internal-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Internal:ApiKey"];
        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(expected) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided)))
        {
            context.Result = new ObjectResult(ApiResponse.Fail("Internal endpoint.")) { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}

public static class InternalHttpExtensions
{
    /// <summary>
    /// Typed HttpClient for another service's internal API. Its base URL comes from
    /// <c>Services:{service}</c> (e.g. <c>Services:Academic = http://localhost:5103</c>).
    /// </summary>
    public static IHttpClientBuilder AddInternalServiceClient<TClient, TImplementation>(
        this IServiceCollection services, IConfiguration configuration, string service)
        where TClient : class
        where TImplementation : class, TClient =>
        services.AddHttpClient<TClient, TImplementation>(client =>
        {
            client.BaseAddress = new Uri(configuration[$"Services:{service}"]
                ?? throw new InvalidOperationException($"Services:{service} is not configured."));
            client.DefaultRequestHeaders.Add(InternalApiAttribute.HeaderName, configuration["Internal:ApiKey"] ?? string.Empty);
            client.Timeout = TimeSpan.FromSeconds(15);
        });

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>GET an internal endpoint that returns <c>ApiResponse&lt;T&gt;</c> and unwrap it.</summary>
    public static async Task<T> GetDataAsync<T>(this HttpClient http, string url, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(Json, ct);
        return body is { Success: true, Data: not null }
            ? body.Data
            : throw new InvalidOperationException($"Internal call {url} failed: {body?.Message}");
    }

    /// <summary>Plan limits/features from the Subscription service (cached for a minute).</summary>
    public static IServiceCollection AddEntitlements(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddInternalServiceClient<IEntitlementsProvider, EntitlementsClient>(configuration, "Subscription");
        return services;
    }
}

internal sealed class EntitlementsClient(HttpClient http, ICacheService cache) : IEntitlementsProvider
{
    public Task<Entitlements> GetAsync(long academyId, CancellationToken ct = default) =>
        cache.GetOrCreateAsync(
            $"entitlements:{academyId}",
            token => http.GetDataAsync<Entitlements>($"internal/academies/{academyId}/entitlements", token),
            TimeSpan.FromMinutes(1),
            ct);
}
