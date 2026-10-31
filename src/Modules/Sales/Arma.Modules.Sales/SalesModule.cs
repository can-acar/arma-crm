using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Sales.Application;
using Arma.Modules.Sales.Infrastructure;
using Arma.Modules.Sales.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Sales;

public sealed class SalesModule : IModule, INavigationContributor
{
    public string Name => "Satış";

    public Assembly PagesAssembly => typeof(SalesModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => SalesPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SalesDbContext>(configuration, SalesDbContext.Schema);
        services.AddScoped<SalesService>();
        services.AddScoped<QuoteService>();
        services.AddScoped<IPersonalDataSource, SalesPersonalData>();
        services.AddScoped<SalesInsights>();
        services.AddScoped<IContactCriteriaProvider>(sp => sp.GetRequiredService<SalesInsights>());
        services.AddScoped<IDashboardContributor>(sp => sp.GetRequiredService<SalesInsights>());
        services.AddSingleton<IContactPanel, SalesPanel>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SalesEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SalesDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Satış hattı", "sales", 25, SalesPermissions.Read),
        new("Teklifler", "sales/quotes", 26, SalesPermissions.Read),
        new("Satış ayarları", "admin/sales", 945, SalesPermissions.Settings),
    ];

    private sealed class SalesPanel : IContactPanel
    {
        public string Key => "sales";

        public string Title => "Satış";

        public int Order => 30;

        public Type ComponentType => typeof(Web.Pages.ContactSales);

        public string? Permission => SalesPermissions.Read;
    }
}
