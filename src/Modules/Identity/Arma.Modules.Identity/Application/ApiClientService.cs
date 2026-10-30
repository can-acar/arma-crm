using System.Security.Cryptography;
using System.Text.Json;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Security;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Arma.Modules.Identity.Application;

public sealed record ApiClientListItem(string ClientId, string DisplayName, IReadOnlyList<string> Permissions);

/// <summary>Yeni istemcinin gizli anahtarı yalnızca bu yanıtta görünür; veritabanında karması tutulur.</summary>
public sealed record CreatedApiClient(string ClientId, string ClientSecret);

/// <summary>
/// Başka sistemlerin (web sitesi, muhasebe yazılımı) API'ye erişmesi için OAuth2 istemci kimlik bilgileri akışı.
/// İstemciye verilen izinler, aldığı erişim belirtecine eklenir.
/// </summary>
public sealed class ApiClientService(IOpenIddictApplicationManager applications, PermissionCatalog catalog)
{
    public const string PermissionsProperty = "arma_permissions";

    public async Task<IReadOnlyList<ApiClientListItem>> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<ApiClientListItem>();
        await foreach (var application in applications.ListAsync(cancellationToken: cancellationToken))
        {
            var clientId = await applications.GetClientIdAsync(application, cancellationToken);
            var name = await applications.GetDisplayNameAsync(application, cancellationToken);
            var permissions = await GetPermissionsAsync(application, cancellationToken);
            result.Add(new ApiClientListItem(clientId!, name ?? clientId!, permissions));
        }

        return result.OrderBy(c => c.DisplayName, StringComparer.CurrentCulture).ToList();
    }

    public async Task<Result<CreatedApiClient>> CreateAsync(string displayName, IEnumerable<string> permissions, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return new Error("apiclient.name_required", "İstemci adı gerekli.");
        }

        var granted = permissions.Where(catalog.Contains).Distinct().Order(StringComparer.Ordinal).ToArray();
        if (granted.Length == 0)
        {
            return new Error("apiclient.permissions_required", "En az bir izin seçin.");
        }

        var clientId = "arma_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        var secret = Base64Url(RandomNumberGenerator.GetBytes(32));

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            ClientType = ClientTypes.Confidential,
            DisplayName = displayName.Trim(),
            Permissions = { Permissions.Endpoints.Token, Permissions.GrantTypes.ClientCredentials },
        };
        descriptor.Properties[PermissionsProperty] = JsonSerializer.SerializeToElement(granted);

        await applications.CreateAsync(descriptor, cancellationToken);
        return new CreatedApiClient(clientId, secret);
    }

    public async Task<bool> DeleteAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var application = await applications.FindByClientIdAsync(clientId, cancellationToken);
        if (application is null)
        {
            return false;
        }

        await applications.DeleteAsync(application, cancellationToken);
        return true;
    }

    /// <summary>İstemcinin izinleri; katalogda artık olmayan izinler atlanır.</summary>
    public async Task<IReadOnlyList<string>> GetPermissionsAsync(object application, CancellationToken cancellationToken = default)
    {
        var properties = await applications.GetPropertiesAsync(application, cancellationToken);
        if (!properties.TryGetValue(PermissionsProperty, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray().Select(v => v.GetString()).OfType<string>().Where(catalog.Contains).ToList();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
