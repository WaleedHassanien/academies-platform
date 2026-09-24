using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.Identity.Application.Abstractions;
using Academies.Identity.Domain.Academies;
using Academies.Identity.Domain.Roles;
using Academies.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Academies.Identity.Infrastructure.Persistence;

/// <summary>Maps only the Identity service's tables in the shared <c>academies</c> database.</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser), IIdentityDbContext
{
    protected override string ServiceName => IdentityServiceInfo.Name;

    public DbSet<Academy> Academies => Set<Academy>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Neither of these is an ITenantEntity: an academy *is* the tenant, and a SuperAdmin user
        // has no academy. Each gets its own tenant filter, which replaces the base soft-delete
        // filter (EF Core 9 allows one per entity), so !IsDeleted is repeated here.
        modelBuilder.Entity<Academy>()
            .HasQueryFilter(a => !a.IsDeleted && (BypassTenantFilter || a.Id == CurrentAcademyId));
        modelBuilder.Entity<User>()
            .HasQueryFilter(u => !u.IsDeleted && (BypassTenantFilter || u.AcademyId == CurrentAcademyId));
    }
}

internal sealed class IdentityDesignTimeFactory() : DesignTimeDbContextFactoryBase<IdentityDbContext>(IdentityServiceInfo.Name)
{
    protected override IdentityDbContext Create(DbContextOptions<IdentityDbContext> options, ICurrentUser currentUser) =>
        new(options, currentUser);
}
