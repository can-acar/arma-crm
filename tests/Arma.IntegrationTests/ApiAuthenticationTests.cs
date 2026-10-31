using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arma.Modules.Identity.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class ApiAuthenticationTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Api_without_token_returns_401()
    {
        RequiresDatabase.Check();
        var response = await factory.CreateClient().GetAsync("/api/v1/contacts", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Client_credentials_token_grants_only_the_clients_permissions()
    {
        RequiresDatabase.Check();
        var client = await factory.WithScopeAsync(sp => sp.GetRequiredService<ApiClientService>().CreateAsync("Test", ["contacts.read"], ct));
        var http = factory.CreateClient();

        var token = await RequestTokenAsync(http, client.Value!.ClientId, client.Value.ClientSecret);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        (await http.GetAsync("/api/v1/contacts", ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var write = await http.PostAsJsonAsync("/api/v1/contacts/people", new { firstName = "Ali", lastName = "Veli" }, ct);
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Client_with_write_permission_can_create_contacts()
    {
        RequiresDatabase.Check();
        var client = await factory.WithScopeAsync(sp => sp.GetRequiredService<ApiClientService>().CreateAsync("Yazıcı", ["contacts.read", "contacts.write"], ct));
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await RequestTokenAsync(http, client.Value!.ClientId, client.Value.ClientSecret));

        var created = await http.PostAsJsonAsync("/api/v1/contacts/people", new { firstName = "Ayşe", lastName = "Işık" }, ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Wrong_secret_is_rejected()
    {
        RequiresDatabase.Check();
        var client = await factory.WithScopeAsync(sp => sp.GetRequiredService<ApiClientService>().CreateAsync("Test", ["contacts.read"], ct));
        var response = await factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = client.Value!.ClientId,
            ["client_secret"] = "yanlis",
        }), ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Pages_redirect_to_setup_until_first_administrator_exists()
    {
        RequiresDatabase.Check();
        var http = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await http.GetAsync("/contacts", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe("/setup");
    }

    private async Task<string> RequestTokenAsync(HttpClient http, string clientId, string secret)
    {
        var response = await http.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = secret,
        }), ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("access_token").GetString()!;
    }
}
