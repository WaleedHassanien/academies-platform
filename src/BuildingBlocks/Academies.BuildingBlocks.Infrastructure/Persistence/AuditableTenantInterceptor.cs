using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Application.Exceptions;
using Academies.BuildingBlocks.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Academies.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Runs before every SaveChanges and handles four things:
/// <list type="bullet">
/// <item>Stamps the US-003a audit fields.</item>
/// <item>Turns deletes into soft deletes.</item>
/// <item>Stamps <see cref="ITenantEntity.AcademyId"/> on insert.</item>
/// <item>Refuses writes into another academy's data (US-008).</item>
/// </list>
/// </summary>
public sealed class AuditableTenantInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedOnUtc = now;
                    entry.Entity.CreatedBy ??= userId;
                    entry.Entity.IsDeleted = false;
                    if (entry.Entity is ITenantEntity tenant)
                    {
                        StampTenant(tenant);
                    }
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedOnUtc = now;
                    entry.Entity.UpdatedBy = userId;
                    if (entry.Entity is ITenantEntity)
                    {
                        // An entity never moves between academies.
                        entry.Property(nameof(ITenantEntity.AcademyId)).IsModified = false;
                    }
                    break;

                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.UpdatedOnUtc = now;
                    entry.Entity.UpdatedBy = userId;
                    break;
            }
        }
    }

    private void StampTenant(ITenantEntity tenant)
    {
        if (tenant.AcademyId == 0)
        {
            tenant.AcademyId = currentUser.AcademyId
                ?? throw new InvalidOperationException(
                    $"Cannot insert {tenant.GetType().Name} without an academy: the caller has no academy_id and none was set.");
            return;
        }

        if (!currentUser.IsSuperAdmin && currentUser.AcademyId is { } own && tenant.AcademyId != own)
        {
            throw new ForbiddenAccessException("Cannot create data in another academy.");
        }
    }
}
