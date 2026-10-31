using Arma.Modules.Identity.Application;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Identity.Web;

internal static class IdentityMiddleware
{
    public const string SetupPath = "/setup";
    public const string TwoFactorSetupPath = "/account/manage/2fa";

    /// <summary>
    /// Kurulum tamamlanmadıysa her sayfa isteğini ilk yönetici ekranına, 2FA'yı açmak zorunda olan
    /// kullanıcıyı da 2FA kurulum ekranına yönlendirir.
    /// </summary>
    public static void Use(IApplicationBuilder app)
    {
        app.Use(async (context, next) =>
        {
            if (IsPassThrough(context.Request.Path))
            {
                await next(context);
                return;
            }

            var setup = context.RequestServices.GetRequiredService<SetupService>();
            if (await setup.IsSetupRequiredAsync(context.RequestAborted))
            {
                if (!context.Request.Path.StartsWithSegments(SetupPath))
                {
                    context.Response.Redirect(SetupPath);
                    return;
                }
            }
            else if (context.User.HasClaim(ArmaClaimTypes.TwoFactorSetupRequired, "true")
                     && context.User.Identity?.AuthenticationType == IdentityConstants.ApplicationScheme
                     && !context.Request.Path.StartsWithSegments("/account"))
            {
                context.Response.Redirect(TwoFactorSetupPath);
                return;
            }

            await next(context);
        });
    }

    private static bool IsPassThrough(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/connect") || path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/_blazor") || path.StartsWithSegments("/_content") || path.StartsWithSegments("/health")
        || path.StartsWithSegments("/alive") || path.StartsWithSegments("/openapi") || Path.HasExtension(path.Value);
}
