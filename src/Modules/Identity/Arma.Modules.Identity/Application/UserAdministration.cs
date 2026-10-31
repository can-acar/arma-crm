using Arma.Modules.Identity.Domain;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Identity.Application;

public sealed record UserListItem(Guid Id, string Email, string DisplayName, bool IsAdministrator, bool TwoFactorEnabled, bool IsLocked, DateTimeOffset CreatedAt);

/// <summary>Yöneticinin kullanıcıları listelemesi, kilitlemesi ve rol vermesi.</summary>
public sealed class UserAdministration(UserManager<ArmaUser> users, ICurrentUser currentUser, TimeProvider clock)
{
    public async Task<IReadOnlyList<UserListItem>> ListAsync(CancellationToken cancellationToken)
    {
        var administrators = (await users.GetUsersInRoleAsync(UserProvisioning.AdministratorRole)).Select(u => u.Id).ToHashSet();
        var now = clock.GetUtcNow();

        var all = await users.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
        return all.Select(u => new UserListItem(
            u.Id, u.Email ?? "", u.DisplayName, administrators.Contains(u.Id), u.TwoFactorEnabled,
            u.LockoutEnd is { } end && end > now, u.CreatedAt)).ToList();
    }

    public Task<Result<bool>> SetLockedAsync(Guid userId, bool locked) =>
        ChangeAsync(userId, async user =>
        {
            if (locked && user.Id == currentUser.UserId)
            {
                return new Error("identity.self_lock", "Kendi hesabınızı kilitleyemezsiniz.");
            }

            // Kalıcı kilit; süreli kilit (hatalı parola) da kaldırılınca sıfırlanır.
            var result = await users.SetLockoutEndDateAsync(user, locked ? DateTimeOffset.MaxValue : null);
            if (result.Succeeded && !locked)
            {
                result = await users.ResetAccessFailedCountAsync(user);
            }

            return result;
        });

    public Task<Result<bool>> SetAdministratorAsync(Guid userId, bool administrator) =>
        ChangeAsync(userId, async user =>
        {
            if (!administrator && user.Id == currentUser.UserId)
            {
                return new Error("identity.self_demote", "Kendi yönetici yetkinizi kaldıramazsınız.");
            }

            var isAdministrator = await users.IsInRoleAsync(user, UserProvisioning.AdministratorRole);
            if (isAdministrator == administrator)
            {
                return IdentityResult.Success;
            }

            return administrator
                ? await users.AddToRoleAsync(user, UserProvisioning.AdministratorRole)
                : await users.RemoveFromRoleAsync(user, UserProvisioning.AdministratorRole);
        });

    public Task<Result<bool>> ResetTwoFactorAsync(Guid userId) =>
        ChangeAsync(userId, async user =>
        {
            var result = await users.SetTwoFactorEnabledAsync(user, false);
            return result.Succeeded ? await users.ResetAuthenticatorKeyAsync(user) : result;
        });

    private async Task<Result<bool>> ChangeAsync(Guid userId, Func<ArmaUser, Task<OneOf>> change)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new Error("identity.not_found", "Kullanıcı bulunamadı.");
        }

        var outcome = await change(user);
        if (outcome.Error is { } error)
        {
            return error;
        }

        if (!outcome.Result!.Succeeded)
        {
            return new Error("identity." + outcome.Result.Errors.First().Code, string.Join(" ", outcome.Result.Errors.Select(e => e.Description)));
        }

        // Açık oturumlar bir sonraki doğrulamada (en geç 1 dakika) düşer ya da yeni rolü alır.
        await users.UpdateSecurityStampAsync(user);
        return true;
    }

    /// <summary>İş kuralı hatası ya da Identity sonucu.</summary>
    private readonly record struct OneOf(IdentityResult? Result, Error? Error)
    {
        public static implicit operator OneOf(IdentityResult result) => new(result, null);

        public static implicit operator OneOf(Error error) => new(null, error);
    }
}
