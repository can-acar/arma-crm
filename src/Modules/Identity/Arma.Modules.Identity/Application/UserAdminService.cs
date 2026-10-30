using Arma.Modules.Identity.Domain;
using Arma.Modules.Identity.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Identity.Application;

public sealed record UserListItem(Guid Id, string FullName, string Email, string? Role, bool IsActive, bool TwoFactorEnabled, bool IsLockedOut, DateTimeOffset? LastLoginAt);

public sealed record CreateUserRequest(string FullName, string Email, string Password, string Role);

public sealed class UserAdminService(ArmaIdentityDbContext db, UserManager<ArmaUser> users, TimeProvider clock)
{
    public async Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Users.AsNoTracking()
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                User = u,
                Role = db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var now = clock.GetUtcNow();
        return rows.Select(r => new UserListItem(
                r.User.Id, r.User.FullName, r.User.Email ?? "", r.Role, r.User.IsActive, r.User.TwoFactorEnabled,
                r.User.LockoutEnd > now, r.User.LastLoginAt))
            .ToList();
    }

    public async Task<Result<Guid>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return new Error("user.name_required", "Ad soyad gerekli.");
        }

        if (!await db.Roles.AnyAsync(r => r.Name == request.Role, cancellationToken))
        {
            return new Error("user.role_not_found", "Seçilen rol bulunamadı.");
        }

        var user = new ArmaUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), FullName = request.FullName.Trim(), EmailConfirmed = true };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return created.ToError();
        }

        await users.AddToRoleAsync(user, request.Role);
        return user.Id;
    }

    public async Task<Result<bool>> ChangeRoleAsync(Guid userId, string role, string? actingUserId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new Error("user.not_found", "Kullanıcı bulunamadı.");
        }

        var current = await users.GetRolesAsync(user);
        if (current.Contains(SystemRoles.Administrator) && role != SystemRoles.Administrator && await IsLastAdministratorAsync(user))
        {
            return new Error("user.last_admin", "Son yöneticinin rolü değiştirilemez.");
        }

        await users.RemoveFromRolesAsync(user, current);
        var added = await users.AddToRoleAsync(user, role);
        if (!added.Succeeded)
        {
            return added.ToError();
        }

        await users.UpdateSecurityStampAsync(user);
        return true;
    }

    /// <summary>Pasifleştirme açık oturumları da kapatır (güvenlik damgası yenilenir).</summary>
    public async Task<Result<bool>> SetActiveAsync(Guid userId, bool active, string? actingUserId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new Error("user.not_found", "Kullanıcı bulunamadı.");
        }

        if (!active && user.Id.ToString() == actingUserId)
        {
            return new Error("user.self_deactivate", "Kendi hesabınızı pasifleştiremezsiniz.");
        }

        if (!active && await users.IsInRoleAsync(user, SystemRoles.Administrator) && await IsLastAdministratorAsync(user))
        {
            return new Error("user.last_admin", "Son aktif yönetici pasifleştirilemez.");
        }

        user.IsActive = active;
        await users.UpdateAsync(user);
        await users.UpdateSecurityStampAsync(user);
        return true;
    }

    public async Task<Result<bool>> UnlockAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new Error("user.not_found", "Kullanıcı bulunamadı.");
        }

        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return true;
    }

    /// <summary>Telefonunu kaybeden kullanıcı için 2FA'yı sıfırlar; bir sonraki girişte yeniden kurar.</summary>
    public async Task<Result<bool>> ResetTwoFactorAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new Error("user.not_found", "Kullanıcı bulunamadı.");
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await users.UpdateSecurityStampAsync(user);
        return true;
    }

    public async Task<Result<bool>> ResetPasswordAsync(Guid userId, string newPassword)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new Error("user.not_found", "Kullanıcı bulunamadı.");
        }

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var reset = await users.ResetPasswordAsync(user, token, newPassword);
        return reset.Succeeded ? true : reset.ToError();
    }

    private async Task<bool> IsLastAdministratorAsync(ArmaUser user)
    {
        var admins = await users.GetUsersInRoleAsync(SystemRoles.Administrator);
        return admins.Count(a => a.IsActive && a.Id != user.Id) == 0;
    }
}
