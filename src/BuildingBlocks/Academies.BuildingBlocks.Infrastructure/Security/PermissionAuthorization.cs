using Academies.Contracts.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Academies.BuildingBlocks.Infrastructure.Security;

/// <summary>Requires a permission code from <see cref="Permissions"/> (US-011). SuperAdmin always passes.</summary>
public sealed class HasPermissionAttribute(string permission) : AuthorizeAttribute(PolicyPrefix + permission)
{
    public const string PolicyPrefix = "perm:";

    public string Permission { get; } = permission;
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>Builds a policy on demand for every <c>perm:*</c> policy name, so none are registered up front.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(policyName);
        }

        var permission = policyName[HasPermissionAttribute.PolicyPrefix.Length..];
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build();
    }
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var user = context.User;
        if (user.HasClaim(AppClaims.Role, Roles.SuperAdmin) || user.HasClaim(AppClaims.Permission, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
