using System.Security.Claims;
using Arma.Modules.Identity.Web.Pages.Account;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Arma.IntegrationTests;

/// <summary>Veritabanı gerektirmeyen yetkilendirme kuralları.</summary>
public sealed class AuthorizationTests
{
    private static readonly PermissionCatalog Catalog = new([new PermissionDefinition("contacts.read", "Kişiler", "Okuma")]);

    [Fact]
    public async Task Known_permission_becomes_a_policy()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()), Catalog);
        (await provider.GetPolicyAsync("contacts.read")).ShouldNotBeNull();
        (await provider.GetPolicyAsync("unknown.permission")).ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handler_requires_matching_permission_claim(bool hasClaim)
    {
        var claims = hasClaim ? new[] { new Claim(ArmaClaimTypes.Permission, "contacts.read") } : [];
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var requirement = new PermissionRequirement("contacts.read");
        var context = new AuthorizationHandlerContext([requirement], user, null);

        await new PermissionHandler().HandleAsync(context);

        context.HasSucceeded.ShouldBe(hasClaim);
    }

    [Theory]
    [InlineData("/contacts", "/contacts")]
    [InlineData("https://evil.example/", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData(null, "/")]
    public void Return_url_stays_on_this_site(string? input, string expected) =>
        Login.SafeReturnUrl(input).ShouldBe(expected);
}
