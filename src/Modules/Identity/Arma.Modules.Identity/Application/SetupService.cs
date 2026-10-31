using Arma.Modules.Identity.Domain;
using Arma.Modules.Identity.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Identity.Application;

public sealed record CreateAdministratorRequest(string FullName, string Email, string Password);

/// <summary>İlk kurulum: henüz hiç kullanıcı yoksa ilk yöneticiyi oluşturur.</summary>
public sealed class SetupService(ArmaIdentityDbContext db, UserManager<ArmaUser> users, SetupState state)
{
    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken = default)
    {
        if (state.IsCompleted)
        {
            return false;
        }

        var hasUsers = await db.Users.AnyAsync(cancellationToken);
        if (hasUsers)
        {
            state.MarkCompleted();
        }

        return !hasUsers;
    }

    public async Task<Result<ArmaUser>> CreateAdministratorAsync(CreateAdministratorRequest request, CancellationToken cancellationToken = default)
    {
        if (!await IsSetupRequiredAsync(cancellationToken))
        {
            return new Error("setup.completed", "Kurulum zaten tamamlanmış.");
        }

        var user = new ArmaUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), FullName = request.FullName.Trim(), EmailConfirmed = true };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return created.ToError();
        }

        await users.AddToRoleAsync(user, SystemRoles.Administrator);
        state.MarkCompleted();
        return user;
    }
}

/// <summary>Kurulumun tamamlandığını bellekte tutar; her istekte veritabanına bakılmaz.</summary>
public sealed class SetupState
{
    private volatile bool completed;

    public bool IsCompleted => completed;

    public void MarkCompleted() => completed = true;
}

internal static class IdentityResultExtensions
{
    public static Error ToError(this IdentityResult result) =>
        new(result.Errors.FirstOrDefault()?.Code ?? "identity.error", string.Join(" ", result.Errors.Select(e => e.Description)));
}
