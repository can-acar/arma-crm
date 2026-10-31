namespace Arma.Modules.Identity.Web;

/// <summary>Girişten sonra yalnızca site içi adreslere dönülür (açık yönlendirme engeli).</summary>
internal static class LocalUrl
{
    public static bool IsLocal(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));

    public static string OrHome(string? url) => IsLocal(url) ? url! : "/";
}
