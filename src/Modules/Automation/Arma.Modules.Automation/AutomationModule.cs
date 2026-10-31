using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Automation.Application;
using Arma.Modules.Automation.Infrastructure;
using Arma.Modules.Automation.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Automation;

public sealed class AutomationModule : IModule, INavigationContributor
{
    public string Name => "Otomasyon";

    public Assembly PagesAssembly => typeof(AutomationModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => AutomationPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AutomationDbContext>(configuration, AutomationDbContext.Schema);
        services.AddScoped<AutomationService>();
        services.AddScoped<IPersonalDataSource, AutomationPersonalData>();
        services.AddScoped<IScheduledJob, SegmentRuleJob>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AutomationEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AutomationDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Otomasyon", "automation", 47, AutomationPermissions.Read),
    ];
}
