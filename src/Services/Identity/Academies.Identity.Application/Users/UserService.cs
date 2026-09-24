using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Application.Models;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Contracts.Subscriptions;
using Academies.Identity.Application.Abstractions;
using Academies.Identity.Application.Academies;
using Academies.Identity.Domain.Users;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Academies.Identity.Application.Users;

public sealed record UserDto(
    long Id, long? AcademyId, string Email, string FullName, string? PhoneNumber, bool IsActive,
    IReadOnlyList<string> Roles, DateTime? LastLoginOnUtc, DateTime CreatedOnUtc);

public sealed record UserQuery(string? Search = null, string? Role = null, bool? IsActive = null, long? AcademyId = null, int Page = 1, int PageSize = 20);

/// <summary><see cref="AcademyId"/> is only read for SuperAdmin; academy admins always create in their own academy.</summary>
public sealed record CreateUserRequest(string FullName, string Email, string? PhoneNumber, string Password, IReadOnlyList<string> Roles, long? AcademyId = null);

public sealed record UpdateUserRequest(string FullName, string? PhoneNumber, IReadOnlyList<string> Roles);

public sealed record SetUserActiveRequest(bool IsActive);

/// <summary>US-017: how much of each plan limit is used, e.g. 140/150 students.</summary>
public sealed record UsageDto(string Key, int Used, int? Limit)
{
    public int? Percent => Limit is > 0 ? (int)Math.Round(Used * 100.0 / Limit.Value) : null;
}

public sealed record AcademyUsageDto(string PlanName, string Status, IReadOnlyList<UsageDto> Items);

/// <summary>Roles an academy can hand out. SuperAdmin is platform-only.</summary>
public static class UserRoleCatalog
{
    public static readonly IReadOnlyList<string> Assignable = Roles.All.Where(r => r != Roles.SuperAdmin).ToArray();
}

public interface IUserService
{
    Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct = default);
    Task<UserDto> GetAsync(long id, CancellationToken ct = default);
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<UserDto> UpdateAsync(long id, UpdateUserRequest request, CancellationToken ct = default);
    Task<UserDto> SetActiveAsync(long id, bool isActive, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);
    Task<AcademyUsageDto> GetUsageAsync(long? academyId, CancellationToken ct = default);
}

/// <summary>User management inside an academy (US-019), with plan limit checks (US-017).</summary>
internal sealed class UserService(
    IIdentityDbContext db,
    IPasswordHasher hasher,
    ICurrentUser currentUser,
    IEntitlementsProvider entitlements,
    IEventPublisher events,
    IAuditTrail audit) : IUserService
{
    /// <summary>Which role counts against which plan limit.</summary>
    private static readonly Dictionary<string, string> RoleLimits = new()
    {
        [Roles.Student] = LimitKeys.Students,
        [Roles.Teacher] = LimitKeys.Teachers,
    };

    public async Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct = default)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = db.Users.AsNoTracking();

        if (currentUser.IsSuperAdmin && query.AcademyId is { } academyId)
        {
            q = q.Where(u => u.AcademyId == academyId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            q = q.Where(u => u.FullName.Contains(query.Search) || u.Email.Contains(query.Search));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            q = q.Where(u => u.UserRoles.Any(ur => ur.Role.Name == query.Role));
        }

        if (query.IsActive is { } active)
        {
            q = q.Where(u => u.IsActive == active);
        }

        var total = await q.CountAsync(ct);
        var users = await q.OrderBy(u => u.FullName)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .ToListAsync(ct);

        return new PagedResult<UserDto>
        {
            Items = users.Select(ToDto).ToList(), Page = page.SafePage, PageSize = page.SafePageSize, TotalCount = total,
        };
    }

    public async Task<UserDto> GetAsync(long id, CancellationToken ct = default) => ToDto(await LoadAsync(id, ct));

    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var academyId = ResolveAcademy(request.AcademyId);
        var roles = NormalizeRoles(request.Roles);

        var normalized = User.Normalize(request.Email);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == normalized && !u.IsDeleted, ct))
        {
            throw new ConflictException($"The email '{request.Email}' is already registered.");
        }

        await EnsureLimitsAsync(academyId, addingUser: true, newRoles: roles, ct);

        var roleEntities = await db.Roles.Where(r => roles.Contains(r.Name)).ToListAsync(ct);
        var user = new User
        {
            AcademyId = academyId,
            Email = request.Email.Trim(),
            NormalizedEmail = normalized,
            FullName = request.FullName.Trim(),
            PhoneNumber = request.PhoneNumber,
        };
        user.PasswordHash = hasher.Hash(user, request.Password);
        user.UserRoles.AddRange(roleEntities.Select(r => new UserRole { Role = r }));
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        // The Id exists only after the first save; the second save flushes the outbox.
        await events.PublishAsync(new UserCreated(user.Id, academyId, user.Email, user.FullName, roles), ct);
        await audit.RecordAsync("users.create", nameof(User), user.Id, new { user.Email, Roles = roles }, ct);
        await db.SaveChangesAsync(ct);

        return ToDto(user);
    }

    public async Task<UserDto> UpdateAsync(long id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = await LoadAsync(id, ct);
        var roles = NormalizeRoles(request.Roles);
        var current = user.UserRoles.Select(ur => ur.Role.Name).ToList();
        var added = roles.Except(current).ToList();
        var removed = current.Except(roles).ToList();

        if (current.Contains(Roles.SuperAdmin))
        {
            throw new ForbiddenAccessException("Platform accounts cannot be edited here.");
        }

        if (user.AcademyId is { } academyId && added.Count > 0)
        {
            await EnsureLimitsAsync(academyId, addingUser: false, newRoles: added, ct);
        }

        user.FullName = request.FullName.Trim();
        user.PhoneNumber = request.PhoneNumber;
        user.UserRoles.RemoveAll(ur => removed.Contains(ur.Role.Name));
        if (added.Count > 0)
        {
            var addedRoles = await db.Roles.Where(r => added.Contains(r.Name)).ToListAsync(ct);
            user.UserRoles.AddRange(addedRoles.Select(r => new UserRole { Role = r }));
        }

        await PublishUpdatedAsync(user, roles, ct);
        if (added.Count > 0 || removed.Count > 0)
        {
            await audit.RecordAsync("users.roles", nameof(User), id, new { Added = added, Removed = removed }, ct);
        }

        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task<UserDto> SetActiveAsync(long id, bool isActive, CancellationToken ct = default)
    {
        var user = await LoadAsync(id, ct);
        if (user.Id == currentUser.UserId && !isActive)
        {
            throw new BusinessRuleException("You cannot deactivate your own account.");
        }

        if (isActive && !user.IsActive && user.AcademyId is { } academyId)
        {
            await EnsureLimitsAsync(academyId, addingUser: true, newRoles: user.UserRoles.Select(ur => ur.Role.Name).ToList(), ct);
        }

        user.IsActive = isActive;
        await PublishUpdatedAsync(user, user.UserRoles.Select(ur => ur.Role.Name).ToList(), ct);
        await audit.RecordAsync(isActive ? "users.activate" : "users.deactivate", nameof(User), id, null, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(user);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var user = await LoadAsync(id, ct);
        if (user.Id == currentUser.UserId)
        {
            throw new BusinessRuleException("You cannot delete your own account.");
        }

        db.Users.Remove(user); // soft delete via the interceptor
        await events.PublishAsync(new UserUpdated(user.Id, user.AcademyId, user.Email, user.FullName, [], false), ct);
        await audit.RecordAsync("users.delete", nameof(User), id, new { user.Email }, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<AcademyUsageDto> GetUsageAsync(long? academyId, CancellationToken ct = default)
    {
        var id = ResolveAcademy(academyId);
        var plan = await entitlements.GetAsync(id, ct);
        var counts = await CountsAsync(id, ct);
        var items = LimitKeys.All.Select(k => new UsageDto(k, counts[k], plan.LimitFor(k))).ToList();
        return new AcademyUsageDto(plan.PlanName, plan.Status, items);
    }

    private async Task EnsureLimitsAsync(long academyId, bool addingUser, IReadOnlyCollection<string> newRoles, CancellationToken ct)
    {
        var counts = await CountsAsync(academyId, ct);
        if (addingUser)
        {
            await entitlements.EnsureWithinLimitAsync(academyId, LimitKeys.Users, counts[LimitKeys.Users], ct: ct);
        }

        foreach (var role in newRoles)
        {
            if (RoleLimits.TryGetValue(role, out var key))
            {
                await entitlements.EnsureWithinLimitAsync(academyId, key, counts[key], ct: ct);
            }
        }
    }

    private async Task<Dictionary<string, int>> CountsAsync(long academyId, CancellationToken ct)
    {
        var active = db.Users.IgnoreQueryFilters().Where(u => u.AcademyId == academyId && u.IsActive && !u.IsDeleted);
        return new Dictionary<string, int>
        {
            [LimitKeys.Users] = await active.CountAsync(ct),
            [LimitKeys.Students] = await active.CountAsync(u => u.UserRoles.Any(ur => !ur.IsDeleted && ur.Role.Name == Roles.Student), ct),
            [LimitKeys.Teachers] = await active.CountAsync(u => u.UserRoles.Any(ur => !ur.IsDeleted && ur.Role.Name == Roles.Teacher), ct),
        };
    }

    private Task PublishUpdatedAsync(User user, IReadOnlyList<string> roles, CancellationToken ct) =>
        events.PublishAsync(new UserUpdated(user.Id, user.AcademyId, user.Email, user.FullName, roles, user.IsActive), ct);

    private long ResolveAcademy(long? requested)
    {
        if (currentUser.IsSuperAdmin)
        {
            return requested ?? throw new BusinessRuleException("Choose the academy for this user.");
        }

        return currentUser.AcademyId ?? throw new ForbiddenAccessException();
    }

    private static List<string> NormalizeRoles(IReadOnlyList<string> roles)
    {
        var normalized = roles.Select(r => UserRoleCatalog.Assignable.FirstOrDefault(a => a.Equals(r, StringComparison.OrdinalIgnoreCase)) ?? r)
            .Distinct()
            .ToList();
        var invalid = normalized.Except(UserRoleCatalog.Assignable).ToList();
        if (invalid.Count > 0)
        {
            throw new BusinessRuleException($"Unknown or forbidden role(s): {string.Join(", ", invalid)}.");
        }

        return normalized;
    }

    private async Task<User> LoadAsync(long id, CancellationToken ct) =>
        await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == id, ct)
        ?? throw new NotFoundException(nameof(User), id);

    private static UserDto ToDto(User u) => new(
        u.Id, u.AcademyId, u.Email, u.FullName, u.PhoneNumber, u.IsActive,
        u.UserRoles.Select(ur => ur.Role.Name).Order().ToList(), u.LastLoginOnUtc, u.CreatedOnUtc);
}

internal sealed class CreateUserValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
        RuleFor(x => x.Password).SetValidator(new PasswordValidator());
        RuleFor(x => x.Roles).NotEmpty().WithMessage("Choose at least one role.");
    }
}

internal sealed class UpdateUserValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
        RuleFor(x => x.Roles).NotEmpty().WithMessage("Choose at least one role.");
    }
}
