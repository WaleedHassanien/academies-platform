using System.Security.Claims;
using System.Security.Cryptography;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Security;
using Academies.Identity.Application.Abstractions;
using Academies.Identity.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using IPasswordHasher = Academies.Identity.Application.Abstractions.IPasswordHasher;

namespace Academies.Identity.Infrastructure.Security;

internal sealed class JwtAccessTokenIssuer(SigningKeyStore keys, IOptions<JwtOptions> options, TimeProvider clock) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public IssuedToken Issue(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(AppClaims.UserId, user.Id.ToString()),
            new(AppClaims.Email, user.Email),
            new(AppClaims.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (user.AcademyId is { } academyId)
        {
            claims.Add(new Claim(AppClaims.AcademyId, academyId.ToString()));
        }

        claims.AddRange(roles.Select(r => new Claim(AppClaims.Role, r)));
        claims.AddRange(permissions.Select(p => new Claim(AppClaims.Permission, p)));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Subject = new ClaimsIdentity(claims),
            SigningCredentials = keys.Credentials,
        });

        return new IssuedToken(token, expires);
    }
}

/// <summary>
/// Refresh tokens are random 256-bit values. Only their SHA-256 is stored, under
/// <c>identity:refresh:{hash}</c>, so a Redis dump can't be replayed.
/// </summary>
internal sealed class RedisRefreshTokenStore(ICacheService cache, IOptions<JwtOptions> options, TimeProvider clock) : IRefreshTokenStore
{
    public async Task<IssuedToken> IssueAsync(long userId, CancellationToken ct = default)
    {
        var lifetime = TimeSpan.FromDays(options.Value.RefreshTokenDays);
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        await cache.SetAsync(Key(token), userId, lifetime, ct);
        return new IssuedToken(token, clock.GetUtcNow().UtcDateTime.Add(lifetime));
    }

    public async Task<long?> ConsumeAsync(string token, CancellationToken ct = default)
    {
        var key = Key(token);
        var userId = await cache.GetAsync<long?>(key, ct);
        if (userId is not null)
        {
            await cache.RemoveAsync(key, ct);
        }

        return userId;
    }

    public Task RevokeAsync(string token, CancellationToken ct = default) => cache.RemoveAsync(Key(token), ct);

    private static string Key(string token) => $"refresh:{Hash(token)}";

    internal static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
}

/// <summary>6-digit reset codes valid for 30 minutes, one active code per user.</summary>
internal sealed class RedisPasswordResetStore(ICacheService cache) : IPasswordResetStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public async Task<string> CreateAsync(long userId, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        await cache.SetAsync(Key(userId), RedisRefreshTokenStore.Hash(code), Lifetime, ct);
        return code;
    }

    public async Task<bool> ConsumeAsync(long userId, string token, CancellationToken ct = default)
    {
        var stored = await cache.GetAsync<string>(Key(userId), ct);
        if (stored is null || !CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(stored),
                System.Text.Encoding.UTF8.GetBytes(RedisRefreshTokenStore.Hash(token.Trim()))))
        {
            return false;
        }

        await cache.RemoveAsync(Key(userId), ct);
        return true;
    }

    private static string Key(long userId) => $"pwdreset:{userId}";
}

internal sealed class AspNetPasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(User user, string password) => _inner.HashPassword(user, password);

    public bool Verify(User user, string password) =>
        !string.IsNullOrEmpty(user.PasswordHash) &&
        _inner.VerifyHashedPassword(user, user.PasswordHash, password) != PasswordVerificationResult.Failed;
}
