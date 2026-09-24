using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Infrastructure.Persistence;
using Academies.Subscription.Application;
using Academies.Subscription.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Academies.Subscription.Infrastructure.Persistence;

/// <summary>Maps only the Subscription service's tables in the shared <c>academies</c> database.</summary>
public sealed class SubscriptionDbContext(DbContextOptions<SubscriptionDbContext> options, ICurrentUser currentUser)
    : ServiceDbContext(options, currentUser), ISubscriptionDbContext
{
    protected override string ServiceName => SubscriptionServiceInfo.Name;

    public DbSet<SubscriptionPlan> Plans => Set<SubscriptionPlan>();
    public DbSet<PlanPriceHistory> PriceHistory => Set<PlanPriceHistory>();
    public DbSet<AcademySubscription> AcademySubscriptions => Set<AcademySubscription>();
    public DbSet<AcademyLimitOverride> LimitOverrides => Set<AcademyLimitOverride>();
}

internal sealed class SubscriptionDesignTimeFactory() : DesignTimeDbContextFactoryBase<SubscriptionDbContext>(SubscriptionServiceInfo.Name)
{
    protected override SubscriptionDbContext Create(DbContextOptions<SubscriptionDbContext> options, ICurrentUser currentUser) => new(options, currentUser);
}

internal sealed class PlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> b)
    {
        b.ToTable("SubscriptionPlans");
        b.Property(x => x.Code).HasMaxLength(30);
        b.Property(x => x.Name).HasMaxLength(100);
        b.Property(x => x.MonthlyPrice).HasPrecision(12, 2);
        b.HasIndex(x => x.Code).IsUnique();
        b.HasMany(x => x.Limits).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Features).WithOne().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class PlanLimitConfiguration : IEntityTypeConfiguration<PlanLimit>
{
    public void Configure(EntityTypeBuilder<PlanLimit> b)
    {
        b.ToTable("PlanLimits");
        b.Property(x => x.LimitKey).HasMaxLength(50);
        b.HasIndex(x => new { x.PlanId, x.LimitKey }).IsUnique();
    }
}

internal sealed class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
{
    public void Configure(EntityTypeBuilder<PlanFeature> b)
    {
        b.ToTable("PlanFeatures");
        b.Property(x => x.FeatureKey).HasMaxLength(50);
        b.HasIndex(x => new { x.PlanId, x.FeatureKey }).IsUnique();
    }
}

internal sealed class PlanPriceHistoryConfiguration : IEntityTypeConfiguration<PlanPriceHistory>
{
    public void Configure(EntityTypeBuilder<PlanPriceHistory> b)
    {
        b.ToTable("PlanPriceHistory");
        b.Property(x => x.OldPrice).HasPrecision(12, 2);
        b.Property(x => x.NewPrice).HasPrecision(12, 2);
        b.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AcademySubscriptionConfiguration : IEntityTypeConfiguration<AcademySubscription>
{
    public void Configure(EntityTypeBuilder<AcademySubscription> b)
    {
        b.ToTable("AcademySubscriptions");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasOne(x => x.Plan).WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.AcademyId).IsUnique();
    }
}

internal sealed class AcademyLimitOverrideConfiguration : IEntityTypeConfiguration<AcademyLimitOverride>
{
    public void Configure(EntityTypeBuilder<AcademyLimitOverride> b)
    {
        b.ToTable("AcademyLimitOverrides");
        b.Property(x => x.LimitKey).HasMaxLength(50);
        b.Property(x => x.Reason).HasMaxLength(300);
        b.HasIndex(x => new { x.AcademyId, x.LimitKey });
    }
}
