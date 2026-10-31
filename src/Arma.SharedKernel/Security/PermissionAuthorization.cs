using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Arma.SharedKernel.Security;

/// <summary>Politika adı bir izin adıysa (<c>contacts.read</c>) o izni isteyen politikayı üretir.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options, PermissionCatalog catalog)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (await base.GetPolicyAsync(policyName) is { } policy)
        {
            return policy;
        }

        return catalog.Contains(policyName)
            ? new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName)).Build()
            : null;
    }
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(ArmaClaimTypes.Permission, requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
