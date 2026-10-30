using Arma.Modules.Identity.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace Arma.Modules.Identity.Web;

internal static class AccountEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Form gönderimi; antiforgery doğrulaması UseAntiforgery ile yapılır.
        endpoints.MapPost("/account/logout", async (SignInManager<ArmaUser> signIn) =>
        {
            await signIn.SignOutAsync();
            return Results.LocalRedirect(IdentityModule.LoginPath);
        });
    }
}
