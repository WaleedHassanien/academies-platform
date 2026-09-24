using Academies.Identity.Domain.Academies;
using Academies.Identity.Domain.Roles;
using Academies.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Academies.Identity.Infrastructure.Persistence;

internal sealed class AcademyConfiguration : IEntityTypeConfiguration<Academy>
{
    public void Configure(EntityTypeBuilder<Academy> b)
    {
        b.ToTable("Academies");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.LogoUrl).HasMaxLength(500);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        b.Property(x => x.PhoneNumber).HasMaxLength(30);
        b.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();

        // Emails are unique platform-wide: login happens before we know the academy.
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.HasIndex(x => x.AcademyId);
        b.HasOne<Academy>().WithMany().HasForeignKey(x => x.AcademyId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("UserRoles");
        b.HasIndex(x => new { x.UserId, x.RoleId }).IsUnique();
        b.HasOne(x => x.User).WithMany(u => u.UserRoles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.NormalizedName).IsUnique();
    }
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("Permissions");
        b.Property(x => x.Code).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

internal sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.ToTable("RolePermissions");
        b.HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique();
        b.HasOne(x => x.Role).WithMany(r => r.RolePermissions).HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Permission).WithMany().HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}
