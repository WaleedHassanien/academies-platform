namespace Academies.BuildingBlocks.Domain;

/// <summary>
/// Root of every persisted entity (US-003a). The five audit/soft-delete fields are filled by
/// <c>AuditableTenantInterceptor</c> and filtered by the base DbContext; never set them by hand.
/// </summary>
public abstract class BaseEntity
{
    public long Id { get; set; }

    public DateTime CreatedOnUtc { get; set; }
    public DateTime? UpdatedOnUtc { get; set; }
    public long? CreatedBy { get; set; }
    public long? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
}
