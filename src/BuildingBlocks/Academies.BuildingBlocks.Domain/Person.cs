namespace Academies.BuildingBlocks.Domain;

/// <summary>
/// Local copy of an Identity user, kept in sync from <c>UserCreated</c>/<c>UserUpdated</c> events.
/// Lets a service show names and emails and filter by role without calling Identity.
/// Mapped to <c>{service}_People</c> by services that opt in.
/// </summary>
public sealed class Person : BaseEntity, ITenantEntity
{
    public long AcademyId { get; set; }
    public long UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>Comma-separated role names, e.g. "Teacher,Supervisor".</summary>
    public string Roles { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public bool HasRole(string role) =>
        Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(role, StringComparer.OrdinalIgnoreCase);
}
