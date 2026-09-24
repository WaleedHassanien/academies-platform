using System.Reflection;
using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace Academies.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Base DbContext for every service. It maps only the service's own tables (configurations
/// from the derived context's assembly), plus the MassTransit outbox tables, and gives each
/// <see cref="BaseEntity"/> root one query filter:
/// <list type="bullet">
/// <item>always: hide <c>IsDeleted</c> rows (US-003a);</item>
/// <item>for <see cref="ITenantEntity"/>: also limit rows to the caller's academy (US-008).</item>
/// </list>
/// EF Core 9 allows a single filter per entity and <c>IgnoreQueryFilters()</c> drops all of it.
/// A query that must cross academies has to re-add <c>!e.IsDeleted</c> itself.
/// </summary>
public abstract class ServiceDbContext(DbContextOptions options, ICurrentUser currentUser) : DbContext(options)
{
    protected ICurrentUser CurrentUser { get; } = currentUser;

    /// <summary>
    /// Owning service, e.g. "identity". All services share one database, so infrastructure
    /// tables every service needs (the MassTransit outbox) get this as a prefix to stay unique.
    /// </summary>
    protected abstract string ServiceName { get; }

    /// <summary>
    /// Opt in to the <see cref="Person"/> read model (<c>{service}_People</c>), fed by
    /// <c>PeopleDirectoryConsumer</c> from Identity's user events.
    /// </summary>
    protected virtual bool HasPeopleDirectory => false;

    // Query filters reference these members of the context instance, so EF evaluates them per
    // query rather than baking the first caller's academy into the cached model.
    protected long? CurrentAcademyId => CurrentUser.AcademyId;
    protected bool BypassTenantFilter => CurrentUser.IsSuperAdmin;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly);

        modelBuilder.AddInboxStateEntity(e => e.ToTable($"{ServiceName}_InboxState"));
        modelBuilder.AddOutboxMessageEntity(e => e.ToTable($"{ServiceName}_OutboxMessage"));
        modelBuilder.AddOutboxStateEntity(e => e.ToTable($"{ServiceName}_OutboxState"));

        if (HasPeopleDirectory)
        {
            modelBuilder.Entity<Person>(e =>
            {
                e.ToTable($"{ServiceName}_People");
                e.Property(p => p.FullName).HasMaxLength(200);
                e.Property(p => p.Email).HasMaxLength(256);
                e.Property(p => p.Roles).HasMaxLength(200);
                e.HasIndex(p => p.UserId).IsUnique();
            });
        }

        ApplyBaseEntityConventions(modelBuilder);
    }

    private void ApplyBaseEntityConventions(ModelBuilder modelBuilder)
    {
        var roots = modelBuilder.Model.GetEntityTypes()
            .Where(t => t.BaseType is null && typeof(BaseEntity).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();

        foreach (var clrType in roots)
        {
            var method = typeof(ITenantEntity).IsAssignableFrom(clrType)
                ? nameof(ConfigureTenantEntity)
                : nameof(ConfigureBaseEntity);

            typeof(ServiceDbContext)
                .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(clrType)
                .Invoke(this, [modelBuilder]);
        }
    }

    private void ConfigureBaseEntity<TEntity>(ModelBuilder modelBuilder) where TEntity : BaseEntity
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.HasQueryFilter(e => !e.IsDeleted);
        entity.HasIndex(e => e.IsDeleted);
    }

    private void ConfigureTenantEntity<TEntity>(ModelBuilder modelBuilder) where TEntity : BaseEntity, ITenantEntity
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.HasQueryFilter(e => !e.IsDeleted && (BypassTenantFilter || e.AcademyId == CurrentAcademyId));
        entity.HasIndex(e => e.IsDeleted);
        entity.HasIndex(e => e.AcademyId);
    }
}
