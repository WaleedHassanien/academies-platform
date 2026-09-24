using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.Contracts.Security;
using Academies.Identity.Application.Abstractions;
using Academies.Identity.Domain.Academies;
using Academies.Identity.Domain.Users;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace Academies.Identity.Application.Auth;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
    Task LogoutAsync(LogoutRequest request, CancellationToken ct = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<UserInfo> GetCurrentAsync(CancellationToken ct = default);
}

/// <summary>Login, token refresh/rotation, logout and password reset (US-010, US-012).</summary>
internal sealed class AuthService(
    IIdentityDbContext db,
    IPasswordHasher hasher,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenStore refreshTokens,
    IPasswordResetStore resetTokens,
    IEmailSender email,
    ICurrentUser currentUser,
    TimeProvider clock) : IAuthService
{
    private const string InvalidResetCode = "The reset code is invalid or has expired.";

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await FindUserAsync(u => u.NormalizedEmail == User.Normalize(request.Email), ct);

        // One message for unknown email, wrong password and inactive user: never reveal which.
        if (user is null || !user.IsActive || !hasher.Verify(user, request.Password))
        {
            throw new UnauthorizedException("Invalid email or password.");
        }

        await EnsureAcademyActiveAsync(user, ct);

        user.LastLoginOnUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        return await IssueAsync(user, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        var userId = await refreshTokens.ConsumeAsync(request.RefreshToken, ct)
            ?? throw new UnauthorizedException("Refresh token is invalid or has expired.");

        var user = await FindUserAsync(u => u.Id == userId, ct);
        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedException("Refresh token is invalid or has expired.");
        }

        await EnsureAcademyActiveAsync(user, ct);
        return await IssueAsync(user, ct);
    }

    public Task LogoutAsync(LogoutRequest request, CancellationToken ct = default) =>
        refreshTokens.RevokeAsync(request.RefreshToken, ct);

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct = default)
    {
        var user = await FindUserAsync(u => u.NormalizedEmail == User.Normalize(request.Email), ct);
        if (user is null || !user.IsActive)
        {
            return; // Same response whether or not the account exists.
        }

        var code = await resetTokens.CreateAsync(user.Id, ct);
        await email.SendAsync(
            user.Email,
            "Password reset",
            $"Your password reset code is: {code}\nIt expires in 30 minutes.",
            ct);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await FindUserAsync(u => u.NormalizedEmail == User.Normalize(request.Email), ct);
        if (user is null || !await resetTokens.ConsumeAsync(user.Id, request.Token, ct))
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Token), InvalidResetCode)]);
        }

        user.PasswordHash = hasher.Hash(user, request.NewPassword);
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserInfo> GetCurrentAsync(CancellationToken ct = default)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException("Authentication is required.");
        var user = await FindUserAsync(u => u.Id == userId, ct) ?? throw new NotFoundException(nameof(User), userId);
        var (roles, permissions) = RolesAndPermissions(user);
        return ToInfo(user, roles, permissions);
    }

    private async Task<AuthResponse> IssueAsync(User user, CancellationToken ct)
    {
        var (roles, permissions) = RolesAndPermissions(user);
        var access = accessTokens.Issue(user, roles, permissions);
        var refresh = await refreshTokens.IssueAsync(user.Id, ct);
        return new AuthResponse(access.Value, access.ExpiresAtUtc, refresh.Value, refresh.ExpiresAtUtc, ToInfo(user, roles, permissions));
    }

    // Login and refresh happen before a token exists, so the tenant filter would hide everyone.
    // IgnoreQueryFilters() also drops soft-delete for the whole query (EF Core 9), so deleted
    // rows are excluded explicitly here and in RolesAndPermissions.
    private Task<User?> FindUserAsync(System.Linq.Expressions.Expression<Func<User, bool>> predicate, CancellationToken ct) =>
        db.Users
            .IgnoreQueryFilters()
            .Where(u => !u.IsDeleted)
            .Include(u => u.UserRoles.Where(ur => !ur.IsDeleted)).ThenInclude(ur => ur.Role)
            .ThenInclude(r => r.RolePermissions.Where(rp => !rp.IsDeleted)).ThenInclude(rp => rp.Permission)
            .SingleOrDefaultAsync(predicate, ct);

    private async Task EnsureAcademyActiveAsync(User user, CancellationToken ct)
    {
        if (user.AcademyId is not { } academyId)
        {
            return;
        }

        var status = await db.Academies
            .IgnoreQueryFilters()
            .Where(a => a.Id == academyId && !a.IsDeleted)
            .Select(a => (AcademyStatus?)a.Status)
            .SingleOrDefaultAsync(ct);

        if (status != AcademyStatus.Active)
        {
            throw new ForbiddenAccessException("This academy is suspended. Contact the platform administrator.");
        }
    }

    private static (string[] Roles, string[] Permissions) RolesAndPermissions(User user)
    {
        var activeRoles = user.UserRoles.Select(ur => ur.Role).Where(r => !r.IsDeleted).ToList();
        var roles = activeRoles.Select(r => r.Name).Distinct().Order().ToArray();
        var permissions = roles.Contains(Roles.SuperAdmin)
            ? Permissions.All.ToArray()
            : activeRoles
                .SelectMany(r => r.RolePermissions)
                .Where(rp => !rp.Permission.IsDeleted)
                .Select(rp => rp.Permission.Code)
                .Distinct()
                .Order()
                .ToArray();
        return (roles, permissions);
    }

    private static UserInfo ToInfo(User user, string[] roles, string[] permissions) =>
        new(user.Id, user.Email, user.FullName, user.AcademyId, roles, permissions);
}
