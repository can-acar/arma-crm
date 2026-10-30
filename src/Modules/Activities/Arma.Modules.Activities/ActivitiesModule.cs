using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Activities.Application;
using Arma.Modules.Activities.Infrastructure;
using Arma.Modules.Activities.Web;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Activities;

public sealed class ActivitiesModule : IModule, INavigationContributor
{
    public string Name => "Aktiviteler";

    public Assembly PagesAssembly => typeof(ActivitiesModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => ActivitiesPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ActivitiesDbContext>(configuration, ActivitiesDbContext.Schema);
        services.AddScoped<ActivityService>();
        services.AddScoped<Arma.SharedKernel.Privacy.IPersonalDataSource, ActivitiesPersonalData>();
        services.AddSingleton<IContactPanel, TimelinePanel>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ActivityEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ActivitiesDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Görevlerim", "tasks", 5, ActivitiesPermissions.Read),
        new("Aktiviteler", "activities", 20, ActivitiesPermissions.Read),
    ];

    private sealed class TimelinePanel : IContactPanel
    {
        public string Key => "timeline";

        public string Title => "Zaman çizelgesi";

        public int Order => 10;

        public Type ComponentType => typeof(Web.Pages.ContactTimeline);

        public string? Permission => ActivitiesPermissions.Read;
    }
}
