using System.Security.Claims;
using Arma.Modules.Identity.Domain;
using Arma.Modules.Identity.Infrastructure;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Identity.Application;

public sealed record RoleListItem(Guid Id, string Name, string? Description, bool IsSystem, bool RequiresTwoFactor, int UserCount, IReadOnlySet<string> Permissions);

public sealed class RoleAdminService(ArmaIdentityDbContext db, RoleManager<ArmaRole> roles, PermissionCatalog catalog)
{
    public async Task<IReadOnlyList<RoleListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var all = await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        var claims = await db.RoleClaims.AsNoTracking().Where(c => c.ClaimType == ArmaClaimTypes.Permission).ToListAsync(cancellationToken);
        var counts = await db.UserRoles.GroupBy(ur => ur.RoleId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return all.Select(r => new RoleListItem(
                r.Id, r.Name!, r.Description, r.IsSystem, r.RequiresTwoFactor, counts.GetValueOrDefault(r.Id),
                r.Name == SystemRoles.Administrator
                    ? catalog.All.Select(p => p.Name).ToHashSet()
                    : claims.Where(c => c.RoleId == r.Id).Select(c => c.ClaimValue!).ToHashSet()))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> NamesAsync(CancellationToken cancellationToken = default) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync(cancellationToken);

    public async Task<Result<Guid>> CreateAsync(string name, string? description, bool requiresTwoFactor)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Error("role.name_required", "Rol adı gerekli.");
        }

        var role = new ArmaRole(name.Trim()) { Description = description?.Trim(), RequiresTwoFactor = requiresTwoFactor };
        var created = await roles.CreateAsync(role);
        return created.Succeeded ? role.Id : created.ToError();
    }

    /// <summary>Rolün izinlerini verilen kümeyle değiştirir. Yönetici rolünün izinleri sabittir.</summary>
    public async Task<Result<bool>> SetPermissionsAsync(Guid roleId, IEnumerable<string> permissions)
    {
        var role = await roles.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            return new Error("role.not_found", "Rol bulunamadı.");
        }

        if (role.Name == SystemRoles.Administrator)
        {
            return new Error("role.admin_fixed", "Yönetici rolü her zaman tüm izinlere sahiptir.");
        }

        var wanted = permissions.Where(catalog.Contains).ToHashSet();
        var current = (await roles.GetClaimsAsync(role)).Where(c => c.Type == ArmaClaimTypes.Permission).ToList();

        foreach (var claim in current.Where(c => !wanted.Contains(c.Value)))
        {
            await roles.RemoveClaimAsync(role, claim);
        }

        foreach (var permission in wanted.Except(current.Select(c => c.Value)))
        {
            await roles.AddClaimAsync(role, new Claim(ArmaClaimTypes.Permission, permission));
        }

        await TouchMembersAsync(role.Id);
        return true;
    }

    public async Task<Result<bool>> SetTwoFactorRequirementAsync(Guid roleId, bool required)
    {
        var role = await roles.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            return new Error("role.not_found", "Rol bulunamadı.");
        }

        if (role.Name == SystemRoles.Administrator && !required)
        {
            return new Error("role.admin_2fa", "Yöneticiler için iki adımlı doğrulama zorunludur.");
        }

        role.RequiresTwoFactor = required;
        await roles.UpdateAsync(role);
        await TouchMembersAsync(role.Id);
        return true;
    }

    public async Task<Result<bool>> DeleteAsync(Guid roleId)
    {
        var role = await roles.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            return new Error("role.not_found", "Rol bulunamadı.");
        }

        if (role.IsSystem)
        {
            return new Error("role.system", "Sistem rolleri silinemez.");
        }

        if (await db.UserRoles.AnyAsync(ur => ur.RoleId == roleId))
        {
            return new Error("role.in_use", "Bu rolde kullanıcı var; önce kullanıcıları başka role taşıyın.");
        }

        var deleted = await roles.DeleteAsync(role);
        return deleted.Succeeded ? true : deleted.ToError();
    }

    /// <summary>İzinler değişince roldeki kullanıcıların çerezi yenilensin diye güvenlik damgasını değiştirir.</summary>
    private async Task TouchMembersAsync(Guid roleId)
    {
        var memberIds = db.UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId);
        await db.Users.Where(u => memberIds.Contains(u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, _ => Guid.NewGuid().ToString()));
    }
}
