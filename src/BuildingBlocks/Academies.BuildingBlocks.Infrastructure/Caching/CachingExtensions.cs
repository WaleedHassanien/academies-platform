using System.Text.Json;
using Academies.BuildingBlocks.Application.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Academies.BuildingBlocks.Infrastructure.Caching;

public static class CachingExtensions
{
    /// <summary>
    /// Registers Redis-backed <see cref="ICacheService"/> from <c>ConnectionStrings:Redis</c>.
    /// Without a Redis connection string it falls back to an in-process cache, which is fine
    /// for unit tests and single-instance dev runs.
    /// </summary>
    public static IServiceCollection AddPlatformCaching(this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var redis = configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
            services.AddHealthChecks().AddRedis(redis, "redis", tags: ["ready"]);
        }

        services.AddSingleton<ICacheService>(sp =>
            new DistributedCacheService(sp.GetRequiredService<IDistributedCache>(), $"{serviceName.ToLowerInvariant()}:"));
        return services;
    }
}

internal sealed class DistributedCacheService(IDistributedCache cache, string prefix) : ICacheService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync(prefix + key, ct);
        return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, Json);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
        cache.SetAsync(
            prefix + key,
            JsonSerializer.SerializeToUtf8Bytes(value, Json),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl ?? DefaultTtl },
            ct);

    public Task RemoveAsync(string key, CancellationToken ct = default) => cache.RemoveAsync(prefix + key, ct);

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var bytes = await cache.GetAsync(prefix + key, ct);
        if (bytes is not null)
        {
            return JsonSerializer.Deserialize<T>(bytes, Json)!;
        }

        var value = await factory(ct);
        await SetAsync(key, value, ttl, ct);
        return value;
    }
}
