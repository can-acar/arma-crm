using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Grants.Application;
using Arma.Modules.Grants.Infrastructure;
using Arma.Modules.Grants.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Grants;

public sealed class GrantsModule : IModule, INavigationContributor
{
    public string Name => "Hibeler";

    public Assembly PagesAssembly => typeof(GrantsModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => GrantsPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<GrantsDbContext>(configuration, GrantsDbContext.Schema);
        services.AddScoped<GrantService>();
        services.AddScoped<IPersonalDataSource, GrantsPersonalData>();
        services.AddScoped<GrantsInsights>();
        services.AddScoped<IContactCriteriaProvider>(sp => sp.GetRequiredService<GrantsInsights>());
        services.AddScoped<IDashboardContributor>(sp => sp.GetRequiredService<GrantsInsights>());
        services.AddSingleton<IContactPanel, GrantsPanel>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => GrantEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GrantsDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Hibeler", "grants", 34, GrantsPermissions.Read),
    ];

    private sealed class GrantsPanel : IContactPanel
    {
        public string Key => "grants";

        public string Title => "Hibeler";

        public int Order => 25;

        public Type ComponentType => typeof(Web.Pages.ContactGrants);

        public string? Permission => GrantsPermissions.Read;
    }
}
