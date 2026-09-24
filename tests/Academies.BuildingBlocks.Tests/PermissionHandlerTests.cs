using System.Security.Claims;
using Academies.BuildingBlocks.Infrastructure.Security;
using Academies.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Shouldly;

namespace Academies.BuildingBlocks.Tests;

public sealed class PermissionHandlerTests   // US-011
{
    private static async Task<bool> Authorize(string required, params Claim[] claims)
    {
        var requirement = new PermissionRequirement(required);
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await new PermissionHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Succeeds_when_permission_claim_present() =>
        (await Authorize(Permissions.Users.Manage, new Claim(AppClaims.Permission, Permissions.Users.Manage))).ShouldBeTrue();

    [Fact]
    public async Task Fails_without_permission_claim() =>
        (await Authorize(Permissions.Users.Manage, new Claim(AppClaims.Permission, Permissions.Users.View))).ShouldBeFalse();

    [Fact]
    public async Task SuperAdmin_passes_every_permission() =>
        (await Authorize(Permissions.Plans.Manage, new Claim(AppClaims.Role, Roles.SuperAdmin))).ShouldBeTrue();

    [Fact]
    public void Every_role_default_references_declared_permissions()
    {
        var unknown = RolePermissionDefaults.Map.Values.SelectMany(p => p).Except(Permissions.All).ToList();
        unknown.ShouldBeEmpty();
    }
}
