using Academies.BuildingBlocks.Domain;

namespace Academies.Identity.Domain.Roles;

/// <summary>Platform-wide role such as Admin or Teacher (US-009). Roles are shared by all academies.</summary>
public sealed class Role : BaseEntity
{
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public bool IsSystem { get; set; }

    public List<RolePermission> RolePermissions { get; set; } = [];
}

public sealed class Permission : BaseEntity
{
    public required string Code { get; set; }
    public string? Description { get; set; }
}

public sealed class RolePermission : BaseEntity
{
    public long RoleId { get; set; }
    public Role Role { get; set; } = null!;

    public long PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
}
