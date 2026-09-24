namespace Academies.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Cache-aside over Redis (US-004). Keys get the service name as a prefix automatically; pass the
/// rest as <c>{entity}:{id}:...</c>, e.g. <c>academy:12:limits</c>.
/// </summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, CancellationToken ct = default);
}
