using Microsoft.AspNetCore.Identity;

namespace Arma.Modules.Identity.Domain;

public sealed class ArmaUser : IdentityUser<Guid>
{
    public ArmaUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public Guid TenantId { get; set; }

    public string FullName { get; set; } = "";

    /// <summary>Pasif kullanıcı oturum açamaz; kayıt denetim izi için silinmez.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }
}
