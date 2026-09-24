using Academies.Identity.Domain.Users;

namespace Academies.Identity.Application.Abstractions;

public sealed record IssuedToken(string Value, DateTime ExpiresAtUtc);

public interface IAccessTokenIssuer
{
    IssuedToken Issue(User user, IReadOnlyCollection<string> roles, IReadOnlyCollection<string> permissions);
}

/// <summary>Opaque refresh tokens kept in Redis so they can be revoked (US-010).</summary>
public interface IRefreshTokenStore
{
    Task<IssuedToken> IssueAsync(long userId, CancellationToken ct = default);

    /// <summary>Returns the owner and revokes the token (rotation). Null if unknown or expired.</summary>
    Task<long?> ConsumeAsync(string token, CancellationToken ct = default);

    Task RevokeAsync(string token, CancellationToken ct = default);
}

/// <summary>Short-lived password reset codes (US-012).</summary>
public interface IPasswordResetStore
{
    Task<string> CreateAsync(long userId, CancellationToken ct = default);
    Task<bool> ConsumeAsync(long userId, string token, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(User user, string password);
    bool Verify(User user, string password);
}
