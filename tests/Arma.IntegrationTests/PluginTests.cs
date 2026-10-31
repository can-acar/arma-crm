using System.Net;
using System.Net.Http.Json;
using Arma.Host;
using Arma.Infrastructure.Plugins;
using Arma.Modules.Contacts.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class PluginTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private static readonly string SampleFolder = Path.Combine(AppContext.BaseDirectory, "plugins", "arma.ornek");

    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Loader_reports_broken_disabled_and_incompatible_plugins_without_failing()
    {
        var root = Directory.CreateTempSubdirectory("arma-plugins-").FullName;
        try
        {
            Plugin(root, "a-good", """{"id":"arma.ornek","name":"Örnek","version":"1.0.0","assembly":"Arma.Plugins.Sample.dll"}""");
            Plugin(root, "b-duplicate", """{"id":"arma.ornek","name":"Kopya","version":"1.0.0","assembly":"Arma.Plugins.Sample.dll"}""");
            Plugin(root, "c-bad-id", """{"id":"Büyük Harf","name":"X","version":"1.0.0","assembly":"Arma.Plugins.Sample.dll"}""");
            Plugin(root, "d-missing", """{"id":"kurum.eksik","name":"Eksik","version":"1.0.0","assembly":"Yok.dll"}""", copyAssembly: false);
            Plugin(root, "e-path", """{"id":"kurum.yol","name":"Yol","version":"1.0.0","assembly":"../Arma.Plugins.Sample.dll"}""");
            Plugin(root, "f-future", """{"id":"kurum.gelecek","name":"Gelecek","version":"1.0.0","assembly":"Arma.Plugins.Sample.dll","minimumArmaVersion":"99.0"}""");
            Plugin(root, "g-off", """{"id":"kurum.kapali","name":"Kapalı","version":"1.0.0","assembly":"Arma.Plugins.Sample.dll"}""");
            Plugin(root, "h-json", "{ bozuk");
            Plugin(root, "i-empty", """{"id":"kurum.bos","name":"Boş","version":"1.0.0","assembly":"Arma.SharedKernel.dll"}""", copyAssembly: false);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Arma.SharedKernel.dll"), Path.Combine(root, "i-empty", "Arma.SharedKernel.dll"));
            Directory.CreateDirectory(Path.Combine(root, "j-not-a-plugin"));

            var results = PluginLoader.LoadAll(root, ["kurum.kapali"], new Version(1, 0), ["Kişiler"]).ToDictionary(r => Path.GetFileName(r.Folder));

            results.Keys.ShouldNotContain("j-not-a-plugin");
            results["a-good"].State.ShouldBe(PluginState.Loaded);
            results["a-good"].Modules.Single().Name.ShouldBe("Örnek eklenti");
            results["b-duplicate"].Error!.ShouldContain("başka bir eklenti");
            results["c-bad-id"].Error!.ShouldContain("Kimlik");
            results["d-missing"].Error!.ShouldContain("bulunamadı");
            results["e-path"].Error!.ShouldContain("dosyasının adı");
            results["f-future"].Error!.ShouldContain("Arma 99.0");
            results["g-off"].State.ShouldBe(PluginState.Disabled);
            results["h-json"].Error!.ShouldContain("okunamadı");
            results["i-empty"].Error!.ShouldContain("IModule");

            PluginLoader.LoadAll(root, [], new Version(1, 0), ["Örnek eklenti"])
                .Single(r => r.Id == "arma.ornek" && r.Folder.EndsWith("a-good")).Error!.ShouldContain("adlı bir modül zaten var");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Plugin_from_the_plugins_folder_adds_permissions_pages_endpoints_and_listens_to_events()
    {
        RequiresDatabase.Check();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var plugin = ArmaModules.Plugins.ShouldHaveSingleItem();
        (plugin.Id, plugin.State, plugin.Manifest!.Name).ShouldBe(("arma.ornek", PluginState.Loaded, "Örnek eklenti"));
        var module = plugin.Modules.Single();
        ArmaModules.All.ShouldContain(module);
        ArmaModules.PageAssemblies.ShouldContain(module.GetType().Assembly);
        module.Permissions.Select(p => p.Name).ShouldBe(["ornek.read"]);

        var ping = await client.GetFromJsonAsync<Dictionary<string, string>>("/api/v1/plugins/ornek/ping", ct);
        ping!["plugin"].ShouldBe("arma.ornek");
        (await client.GetAsync("/api/v1/plugins", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/plugins/ornek/merges", ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var counterType = module.GetType().Assembly.GetType("Arma.Plugins.Sample.MergeCounter")!;
        var counter = factory.Services.GetRequiredService(counterType);
        int Count() => (int)counterType.GetProperty("Count")!.GetValue(counter)!;
        var before = Count();

        var suffix = Guid.NewGuid().ToString("N")[..6];
        var primary = await Person("Eklenti", "Asıl" + suffix);
        var duplicate = await Person("Eklenti", "Asıl" + suffix);
        await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactService>().MergeAsync(primary, duplicate, ct).Value());

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Count() == before && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, ct);
        }

        Count().ShouldBe(before + 1);
    }

    private Task<Guid> Person(string first, string last) =>
        factory.WithScopeAsync(sp => sp.GetRequiredService<ContactService>().CreatePersonAsync(new CreatePersonRequest(first, last, null, null, null), ct).Value());

    private static void Plugin(string root, string folder, string manifest, bool copyAssembly = true)
    {
        var path = Directory.CreateDirectory(Path.Combine(root, folder)).FullName;
        File.WriteAllText(Path.Combine(path, PluginLoader.ManifestFile), manifest);
        if (copyAssembly)
        {
            File.Copy(Path.Combine(SampleFolder, "Arma.Plugins.Sample.dll"), Path.Combine(path, "Arma.Plugins.Sample.dll"));
        }
    }
}
