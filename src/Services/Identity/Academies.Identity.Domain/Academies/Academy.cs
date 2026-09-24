using Academies.BuildingBlocks.Domain;

namespace Academies.Identity.Domain.Academies;

/// <summary>A tenant of the platform (US-007). Every operational row elsewhere carries its Id as AcademyId.</summary>
public sealed class Academy : BaseEntity
{
    public required string Name { get; set; }
    public string? LogoUrl { get; set; }
    public string? Address { get; set; }
    public AcademyStatus Status { get; set; } = AcademyStatus.Active;
}

public enum AcademyStatus
{
    Active = 1,
    Suspended = 2,
}
