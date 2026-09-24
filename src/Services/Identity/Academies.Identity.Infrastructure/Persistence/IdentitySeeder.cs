using Academies.BuildingBlocks.Application.Abstractions;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Events;
using Academies.Contracts.Security;
using Academies.Identity.Application.Abstractions;
using Academies.Identity.Domain.Academies;
using Academies.Identity.Domain.Roles;
using Academies.Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Academies.Identity.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public const string Section = "Seed";

    public bool Enabled { get; set; }
    public string SuperAdminEmail { get; set; } = "superadmin@academies.local";
    public string SuperAdminPassword { get; set; } = string.Empty;

    /// <summary>Creates a demo academy with one Admin (development only).</summary>
    public bool DemoAcademy { get; set; }
    public string DemoAdminEmail { get; set; } = "admin@demo.academies.local";
    public string DemoAdminPassword { get; set; } = string.Empty;
}

/// <summary>
/// Idempotent seed of roles, permissions, the role→permission map (US-009), the platform
/// SuperAdmin and, optionally, a demo academy. It only adds what is missing and never
/// removes a permission an admin granted by hand.
/// </summary>
public static class IdentitySeeder
{
    public static async Task SeedIdentityAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IConfiguration>().GetSection(SeedOptions.Section).Get<SeedOptions>() ?? new();
        if (!options.Enabled)
        {
            return;
        }

        using var _ = CurrentUserOverride.Begin(SystemCurrentUser.Platform);
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(IdentitySeeder));

        await SeedPermissionsAndRolesAsync(db);

        if (!string.IsNullOrWhiteSpace(options.SuperAdminPassword))
        {
            await EnsureUserAsync(db, hasher, options.SuperAdminEmail, "Platform Owner", options.SuperAdminPassword, Roles.SuperAdmin, academyId: null, events);
        }
        else
        {
            logger.LogWarning("Seed:SuperAdminPassword is empty; skipping SuperAdmin seed.");
        }

        if (options.DemoAcademy && !string.IsNullOrWhiteSpace(options.DemoAdminPassword))
        {
            var academy = await db.Academies.FirstOrDefaultAsync(a => a.Name == "Demo Academy");
            if (academy is null)
            {
                academy = new Academy { Name = "Demo Academy", Address = "Riyadh" };
                db.Academies.Add(academy);
                await db.SaveChangesAsync();
                await events.PublishAsync(new AcademyCreated(academy.Id, academy.Name));
                await db.SaveChangesAsync();
            }

            await EnsureUserAsync(db, hasher, options.DemoAdminEmail, "Demo Admin", options.DemoAdminPassword, Roles.Admin, academy.Id, events);
        }

        logger.LogInformation("Identity seed complete.");
    }

    private static async Task SeedPermissionsAndRolesAsync(IdentityDbContext db)
    {
        var existingPermissions = await db.Permissions.ToDictionaryAsync(p => p.Code);
        foreach (var code in Permissions.All.Where(c => !existingPermissions.ContainsKey(c)))
        {
            var permission = new Permission { Code = code };
            db.Permissions.Add(permission);
            existingPermissions[code] = permission;
        }

        var existingRoles = await db.Roles.Include(r => r.RolePermissions).ToDictionaryAsync(r => r.Name);
        foreach (var name in Roles.All.Where(n => !existingRoles.ContainsKey(n)))
        {
            var role = new Role { Name = name, NormalizedName = name.ToUpperInvariant(), IsSystem = true };
            db.Roles.Add(role);
            existingRoles[name] = role;
        }

        await db.SaveChangesAsync();

        foreach (var (roleName, codes) in RolePermissionDefaults.Map)
        {
            var role = existingRoles[roleName];
            var granted = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();
            foreach (var code in codes)
            {
                var permission = existingPermissions[code];
                if (!granted.Contains(permission.Id))
                {
                    role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
                }
            }
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureUserAsync(
        IdentityDbContext db, IPasswordHasher hasher, string email, string fullName, string password, string roleName, long? academyId, IEventPublisher events)
    {
        var normalized = User.Normalize(email);
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized))
        {
            return;
        }

        var role = await db.Roles.SingleAsync(r => r.Name == roleName);
        var user = new User { Email = email.Trim(), NormalizedEmail = normalized, FullName = fullName, AcademyId = academyId };
        user.PasswordHash = hasher.Hash(user, password);
        user.UserRoles.Add(new UserRole { Role = role });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await events.PublishAsync(new UserCreated(user.Id, academyId, user.Email, user.FullName, [roleName]));
        await db.SaveChangesAsync();
    }
}
