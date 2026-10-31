using Arma.Modules.Identity.Domain;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.Identity;

namespace Arma.Modules.Identity.Application;

/// <summary>Kurulumda ilk yöneticiyi ve sonraki kullanıcıları oluşturur.</summary>
public sealed class UserProvisioning(UserManager<ArmaUser> users)
{
    public const string AdministratorRole = "Yönetici";

    public async Task<Result<Guid>> CreateAsync(string email, string displayName, string password, bool administrator, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new ArmaUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email.Trim(),
            Email = email.Trim(),
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? email.Trim() : displayName.Trim(),
        };

        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            return ToError(created);
        }

        if (administrator)
        {
            var role = await users.AddToRoleAsync(user, AdministratorRole);
            if (!role.Succeeded)
            {
                return ToError(role);
            }
        }

        return user.Id;
    }

    private static Error ToError(IdentityResult result) =>
        new("identity." + (result.Errors.FirstOrDefault()?.Code ?? "unknown"), string.Join(" ", result.Errors.Select(e => e.Description)));
}
