using Arma.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arma.Modules.Identity.Infrastructure;

/// <summary>Pasif kullanıcıların oturum açmasını engeller.</summary>
internal sealed class ArmaSignInManager(
    UserManager<ArmaUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ArmaUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ArmaUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ArmaUser> confirmation)
    : SignInManager<ArmaUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    public override async Task<bool> CanSignInAsync(ArmaUser user) =>
        user.IsActive && await base.CanSignInAsync(user);
}
