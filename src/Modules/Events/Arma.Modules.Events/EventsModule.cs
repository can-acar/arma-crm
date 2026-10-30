using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Events.Application;
using Arma.Modules.Events.Infrastructure;
using Arma.Modules.Events.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Events;

public sealed class EventsModule : IModule, INavigationContributor
{
    public string Name => "Etkinlikler";

    public Assembly PagesAssembly => typeof(EventsModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => EventsPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EventsDbContext>(configuration, EventsDbContext.Schema);
        services.AddScoped<EventService>();
        services.AddScoped<EventReminderJob>();
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<EventReminderJob>());
        services.AddScoped<IPersonalDataSource, EventsPersonalData>();
        services.AddScoped<EventsInsights>();
        services.AddScoped<IContactCriteriaProvider>(sp => sp.GetRequiredService<EventsInsights>());
        services.AddScoped<IDashboardContributor>(sp => sp.GetRequiredService<EventsInsights>());
        services.AddSingleton<IContactPanel, EventsPanel>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => EventsEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EventsDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Etkinlikler", "events", 38, EventsPermissions.Read),
    ];

    private sealed class EventsPanel : IContactPanel
    {
        public string Key => "events";

        public string Title => "Etkinlikler";

        public int Order => 28;

        public Type ComponentType => typeof(Web.Pages.ContactEvents);

        public string? Permission => EventsPermissions.Read;
    }
}
