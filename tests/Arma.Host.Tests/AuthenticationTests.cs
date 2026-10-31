using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Arma.Host.Tests;

/// <summary>Oturum açılmadan erişilebilen ve erişilemeyen adresler. Veritabanı gerekmez.</summary>
public class AuthenticationTests(AuthenticationTests.HostFactory factory) : IClassFixture<AuthenticationTests.HostFactory>
{
    [Fact]
    public async Task Api_returns_401_without_session()
    {
        var response = await factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }).GetAsync("/api/v1/contacts", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Pages_redirect_to_login_without_session()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/contacts", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.AbsolutePath.ShouldBe("/account/login");
    }

    [Fact]
    public async Task Login_page_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/account/login", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("Giriş yap");
    }

    [Fact]
    public async Task Security_page_requires_session()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/account/security", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Liveness_check_is_public()
    {
        var response = await factory.CreateClient().GetAsync("/alive", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public sealed class HostFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:arma", "Host=localhost;Database=arma_test");
            builder.UseSetting("Arma:TenantId", "0193a6f0-0000-7000-8000-000000000001");
            builder.UseSetting("Arma:MigrateOnStartup", "false");

            // Anahtarlar normalde veritabanında; testte bellekte tutulur.
            builder.ConfigureTestServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
        }
    }
}
