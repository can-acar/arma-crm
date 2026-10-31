using System.Security.Claims;
using Arma.Modules.Identity.Application;
using Arma.Modules.Identity.Domain;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Arma.Modules.Identity.Web;

internal static class IdentityEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/account/logout", async (SignInManager<ArmaUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.LocalRedirect("~/account/login");
        });

        endpoints.MapPost("/connect/token", IssueTokenAsync).AllowAnonymous().ExcludeFromDescription();
    }

    /// <summary>
    /// OpenIddict istemciyi doğruladıktan sonra çağrılır; belirtece istemcinin izinlerini ekler.
    /// Yalnızca istemci kimlik bilgileri (client_credentials) akışı desteklenir.
    /// </summary>
    private static async Task<IResult> IssueTokenAsync(HttpContext context, IOpenIddictApplicationManager applications, ApiClientService clients)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict isteği bulunamadı.");

        if (!request.IsClientCredentialsGrantType())
        {
            return Results.Forbid(Unsupported(Errors.UnsupportedGrantType), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!, context.RequestAborted);
        if (application is null)
        {
            return Results.Forbid(Unsupported(Errors.InvalidClient), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, request.ClientId);
        identity.AddClaim(new Claim(Claims.Name, await applications.GetDisplayNameAsync(application, context.RequestAborted) ?? request.ClientId!));
        foreach (var permission in await clients.GetPermissionsAsync(application, context.RequestAborted))
        {
            identity.AddClaim(new Claim(ArmaClaimTypes.Permission, permission));
        }

        identity.SetDestinations(_ => [Destinations.AccessToken]);
        return Results.SignIn(new ClaimsPrincipal(identity), authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static AuthenticationProperties Unsupported(string error) => new(new Dictionary<string, string?>
    {
        [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
    });
}
