using System.Security.Claims;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.Contracts.Security;
using Microsoft.AspNetCore.Http;

namespace Academies.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Reads the caller from the request's JWT. Code with no request (consumers, scheduled jobs,
/// seeding) can wrap its work in <see cref="CurrentUserOverride.Begin"/> to act as a system user.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ICurrentUser? Override => CurrentUserOverride.Current;
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Override?.IsAuthenticated ?? Principal?.Identity?.IsAuthenticated == true;
    public long? UserId => Override is not null ? Override.UserId : ReadLong(AppClaims.UserId);
    public long? AcademyId => Override is not null ? Override.AcademyId : ReadLong(AppClaims.AcademyId);
    public IReadOnlyCollection<string> Roles => Override?.Roles ?? ReadAll(AppClaims.Role);
    public IReadOnlyCollection<string> Permissions => Override?.Permissions ?? ReadAll(AppClaims.Permission);
    public bool IsSuperAdmin => IsInRole(Contracts.Security.Roles.SuperAdmin);

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public bool HasPermission(string permission) => IsSuperAdmin || Permissions.Contains(permission);

    private long? ReadLong(string claim) =>
        long.TryParse(Principal?.FindFirst(claim)?.Value, out var value) ? value : null;

    private string[] ReadAll(string claim) =>
        Principal?.FindAll(claim).Select(c => c.Value).ToArray() ?? [];
}

/// <summary>A fixed identity for work that runs outside an HTTP request.</summary>
public sealed class SystemCurrentUser(long? academyId, bool isSuperAdmin, long? userId = null) : ICurrentUser
{
    /// <summary>Platform-wide system user: bypasses tenant filters.</summary>
    public static readonly SystemCurrentUser Platform = new(null, isSuperAdmin: true);

    /// <summary>System user scoped to one academy: tenant filters still apply.</summary>
    public static SystemCurrentUser ForAcademy(long academyId) => new(academyId, isSuperAdmin: false);

    public bool IsAuthenticated => true;
    public long? UserId { get; } = userId;
    public long? AcademyId { get; } = academyId;
    public IReadOnlyCollection<string> Roles { get; } = isSuperAdmin ? [Contracts.Security.Roles.SuperAdmin] : [];
    public IReadOnlyCollection<string> Permissions { get; } = [];
    public bool IsSuperAdmin { get; } = isSuperAdmin;

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    public bool HasPermission(string permission) => IsSuperAdmin;
}

/// <summary>Ambient (async-local) override of the current user.</summary>
public static class CurrentUserOverride
{
    private static readonly AsyncLocal<ICurrentUser?> Ambient = new();

    public static ICurrentUser? Current => Ambient.Value;

    public static IDisposable Begin(ICurrentUser user)
    {
        var previous = Ambient.Value;
        Ambient.Value = user;
        return new Restore(previous);
    }

    private sealed class Restore(ICurrentUser? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
