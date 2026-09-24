using Academies.BuildingBlocks.Domain;
using Academies.Identity.Domain.Roles;

namespace Academies.Identity.Domain.Users;

/// <summary>
/// A login account (US-009). <see cref="AcademyId"/> is null only for platform users (SuperAdmin).
/// That is why this entity does not implement <see cref="ITenantEntity"/>; IdentityDbContext gives
/// it its own nullable tenant filter instead.
/// </summary>
public sealed class User : BaseEntity
{
    public long? AcademyId { get; set; }

    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginOnUtc { get; set; }

    public List<UserRole> UserRoles { get; set; } = [];

    public static string Normalize(string email) => email.Trim().ToUpperInvariant();
}

public sealed class UserRole : BaseEntity
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;

    public long RoleId { get; set; }
    public Role Role { get; set; } = null!;
}
