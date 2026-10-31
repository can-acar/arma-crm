using System.Security.Claims;

namespace Arma.SharedKernel.Security;

/// <summary>İşlemi yapan kullanıcı veya API istemcisi. Denetim izi ve CreatedBy alanları buradan doldurulur.</summary>
public interface ICurrentUser
{
    ClaimsPrincipal Principal { get; }

    bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    /// <summary>Kullanıcı kimliği veya API istemcisinin client_id değeri.</summary>
    string? Id => Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? Principal.FindFirstValue("sub");

    string? Name => Principal.Identity?.Name;

    bool HasPermission(string permission) => Principal.HasClaim(ArmaClaimTypes.Permission, permission);
}
