using Microsoft.AspNetCore.Identity;

namespace Arma.Modules.Identity.Domain;

/// <summary>Uygulama kullanıcısı. Her kullanıcı tek bir kuruma aittir.</summary>
public sealed class ArmaUser : IdentityUser<Guid>
{
    public Guid TenantId { get; set; }

    public string DisplayName { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
