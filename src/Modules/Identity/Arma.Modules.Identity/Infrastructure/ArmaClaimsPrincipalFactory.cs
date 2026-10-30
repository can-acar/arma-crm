using System.Security.Claims;
using Arma.Modules.Identity.Domain;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Arma.Modules.Identity.Infrastructure;

/// <summary>
/// Oturum çerezine izinleri ekler. Rollerin izinleri <see cref="ArmaClaimTypes.Permission"/> türünde rol talebi olarak
/// saklanır; Yönetici rolü kayıtlı bütün izinleri alır.
/// </summary>
internal sealed class ArmaClaimsPrincipalFactory(
    UserManager<ArmaUser> userManager,
    RoleManager<ArmaRole> roleManager,
    IOptions<IdentityOptions> options,
    PermissionCatalog catalog)
    : UserClaimsPrincipalFactory<ArmaUser, ArmaRole>(userManager, roleManager, options)
{
    public const string FullNameClaim = "arma:full_name";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ArmaUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(FullNameClaim, user.FullName));

        var roleNames = await UserManager.GetRolesAsync(user);
        if (roleNames.Contains(SystemRoles.Administrator))
        {
            var existing = identity.FindAll(ArmaClaimTypes.Permission).Select(c => c.Value).ToHashSet();
            foreach (var permission in catalog.All.Where(p => !existing.Contains(p.Name)))
            {
                identity.AddClaim(new Claim(ArmaClaimTypes.Permission, permission.Name));
            }
        }

        if (!user.TwoFactorEnabled && await RequiresTwoFactorAsync(roleNames))
        {
            identity.AddClaim(new Claim(ArmaClaimTypes.TwoFactorSetupRequired, "true"));
        }

        return identity;
    }

    private async Task<bool> RequiresTwoFactorAsync(IEnumerable<string> roleNames)
    {
        foreach (var name in roleNames)
        {
            if (await RoleManager.FindByNameAsync(name) is { RequiresTwoFactor: true })
            {
                return true;
            }
        }

        return false;
    }
}
