using System.Reflection;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Plugins.Sample;

/// <summary>
/// Eklentinin giriş noktası. Arma, eklenti derlemesinde <see cref="IModule"/> uygulayan her sınıfı yerleşik modüller gibi
/// çalıştırır: servisler kaydedilir, uç noktalar eşlenir, sayfalar yönlendiriciye eklenir, izinler rol ekranına düşer.
/// </summary>
public sealed class SampleModule : IModule, INavigationContributor
{
    public const string ReadPermission = "ornek.read";

    public string Name => "Örnek eklenti";

    public Assembly PagesAssembly => typeof(SampleModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions =>
        [new(ReadPermission, "Örnek eklenti", "Örnek eklentinin sayfasını ve kişi kartı sekmesini görme")];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<MergeCounter>();
        services.AddSingleton<INavigationContributor>(this);
        services.AddSingleton<IContactPanel, SamplePanel>();
        services.AddSingleton<IDashboardContributor, SampleDashboard>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Herkese açık bir uç: eklentinin yüklü olduğunu ve sürümünü söyler.
        endpoints.MapGet("/api/v1/plugins/ornek/ping", () => Results.Ok(new { plugin = "arma.ornek", version = typeof(SampleModule).Assembly.GetName().Version?.ToString(3) }))
            .WithTags("Örnek eklenti")
            .AllowAnonymous();

        endpoints.MapGet("/api/v1/plugins/ornek/merges", (MergeCounter counter) => Results.Ok(new { count = counter.Count }))
            .WithTags("Örnek eklenti")
            .RequireAuthorization(ReadPermission);
    }

    /// <summary>Kendi tablosu olan bir eklenti burada şemasını günceller (bkz. docs/eklentiler.md).</summary>
    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;

    public IEnumerable<NavigationItem> GetNavigationItems() => [new("Örnek eklenti", "plugins/ornek", 500, ReadPermission)];

    private sealed class SamplePanel : IContactPanel
    {
        public string Key => "ornek";

        public string Title => "Örnek";

        public int Order => 90;

        public Type ComponentType => typeof(Pages.SampleContactPanel);

        public string? Permission => ReadPermission;
    }

    private sealed class SampleDashboard(MergeCounter counter) : IDashboardContributor
    {
        public Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DashboardTile>>(
                [new DashboardTile("Birleştirilen kişi", counter.Count.ToString(), "Uygulama açıldığından beri (örnek eklenti)", "plugins/ornek", 990, ReadPermission)]);
    }
}

/// <summary>Uygulama açıldığından beri kaç mükerrer kişinin birleştirildiğini sayar.</summary>
public sealed class MergeCounter
{
    private int count;

    public int Count => count;

    public void Increment() => Interlocked.Increment(ref count);
}

/// <summary>
/// Modüllerin yayımladığı olayları dinleme örneği. Arma, eklenti derlemesindeki <c>Handle</c> metotlarını Wolverine ile
/// kendiliğinden bulur; olay sözleşmeleri modüllerin Contracts derlemelerindedir.
/// </summary>
public static class ContactsMergedHandler
{
    public static void Handle(ContactsMerged message, MergeCounter counter) => counter.Increment();
}
