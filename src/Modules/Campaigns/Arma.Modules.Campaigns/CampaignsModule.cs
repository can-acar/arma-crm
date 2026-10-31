using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Campaigns.Application;
using Arma.Modules.Campaigns.Infrastructure;
using Arma.Modules.Campaigns.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Campaigns;

public sealed class CampaignsModule : IModule, INavigationContributor
{
    public string Name => "Kampanyalar";

    public Assembly PagesAssembly => typeof(CampaignsModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => CampaignsPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CampaignsDbContext>(configuration, CampaignsDbContext.Schema);
        services.AddScoped<CampaignService>();
        services.AddScoped<IDashboardContributor, CampaignInsights>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CampaignEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CampaignsDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Kampanyalar", "campaigns", 28, CampaignsPermissions.Read),
    ];
}
