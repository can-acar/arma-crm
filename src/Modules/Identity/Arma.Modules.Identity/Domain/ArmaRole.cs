using Microsoft.AspNetCore.Identity;

namespace Arma.Modules.Identity.Domain;

public sealed class ArmaRole : IdentityRole<Guid>
{
    public ArmaRole()
    {
        Id = Guid.CreateVersion7();
    }

    public ArmaRole(string name) : this()
    {
        Name = name;
    }

    public Guid TenantId { get; set; }

    public string? Description { get; set; }

    /// <summary>Kurulumla gelen rol; silinemez, adı değiştirilemez.</summary>
    public bool IsSystem { get; set; }

    /// <summary>Bu roldeki kullanıcılar iki adımlı doğrulamayı açmadan çalışamaz.</summary>
    public bool RequiresTwoFactor { get; set; }
}
