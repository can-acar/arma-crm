using System.Reflection;
using Arma.Modules.Portal.Application;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Portal;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Portal;

/// <summary>Bağışçıların e-posta bağlantısıyla girip kendi kayıtlarını gördüğü portal. Kendi tablosu yoktur.</summary>
public sealed class PortalModule : IModule
{
    public string Name => "Bağışçı portalı";

    public Assembly PagesAssembly => typeof(PortalModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => [];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddScoped<PortalService>();
        services.AddScoped<IPortalSession>(sp => sp.GetRequiredService<PortalService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }

    public Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken) => Task.CompletedTask;
}
