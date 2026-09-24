namespace Academies.BuildingBlocks.Domain;

/// <summary>
/// Marks an entity as owned by one academy (US-008). The base DbContext adds a "Tenant" query
/// filter and the interceptor stamps <see cref="AcademyId"/> on insert from the current user.
/// </summary>
public interface ITenantEntity
{
    long AcademyId { get; set; }
}
