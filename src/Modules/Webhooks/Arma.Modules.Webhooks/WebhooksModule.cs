using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Webhooks.Application;
using Arma.Modules.Webhooks.Infrastructure;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Webhooks;

public sealed class WebhooksModule : IModule, INavigationContributor
{
    public string Name => "Webhook";

    public Assembly PagesAssembly => typeof(WebhooksModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => WebhooksPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<WebhooksDbContext>(configuration, WebhooksDbContext.Schema);
        services.AddScoped<WebhookService>();
        services.AddScoped<IScheduledJob, WebhookDeliveryJob>();
        services.AddHttpClient(WebhookService.HttpClientName, c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Arma-CRM-Webhook/1.0");
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WebhooksDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Webhook'lar", "admin/webhooks", 925, WebhooksPermissions.Manage),
    ];
}
