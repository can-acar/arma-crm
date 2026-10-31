using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;

namespace Arma.Host.Plugins;

/// <summary>Yüklü eklentileri yönetim ekranında listeler (sayfası Host derlemesindedir, ayrıca eklenmez). Eklentilerin kendisi <see cref="ArmaModules.LoadPlugins"/> ile yüklenir.</summary>
public sealed class PluginsModule : IModule, INavigationContributor
{
    public const string ReadPermission = "plugins.read";

    public string Name => "Eklentiler";

    public IEnumerable<PermissionDefinition> Permissions =>
        [new(ReadPermission, "Eklentiler", "Yüklü eklentileri ve yükleme hatalarını görüntüleme")];

    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddSingleton<INavigationContributor>(this);

    public void MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/v1/plugins", () => ArmaModules.Plugins.Select(p => new
            {
                p.Id,
                p.Manifest?.Name,
                p.Manifest?.Version,
                p.Manifest?.Author,
                State = p.State.ToString(),
                p.Error,
                Modules = p.Modules.Select(m => m.Name),
            }))
            .WithTags("Eklentiler")
            .RequireAuthorization(ReadPermission);

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;

    public IEnumerable<NavigationItem> GetNavigationItems() => [new("Eklentiler", "admin/plugins", 930, ReadPermission)];
}
