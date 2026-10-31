using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Cases.Application;
using Arma.Modules.Cases.Infrastructure;
using Arma.Modules.Cases.Web;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Cases;

public sealed class CasesModule : IModule, INavigationContributor
{
    public string Name => "Vakalar";

    public Assembly PagesAssembly => typeof(CasesModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => CasesPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CasesDbContext>(configuration, CasesDbContext.Schema);
        services.AddScoped<CaseService>();
        services.AddScoped<IPersonalDataSource, CasesPersonalData>();
        services.AddScoped<CasesInsights>();
        services.AddScoped<IContactCriteriaProvider>(sp => sp.GetRequiredService<CasesInsights>());
        services.AddScoped<IDashboardContributor>(sp => sp.GetRequiredService<CasesInsights>());
        services.AddSingleton<IContactPanel, CasesPanel>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CaseEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CasesDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Vakalar", "cases", 22, CasesPermissions.Read),
        new("Vaka türleri", "admin/cases", 947, CasesPermissions.Settings),
    ];

    private sealed class CasesPanel : IContactPanel
    {
        public string Key => "cases";

        public string Title => "Vakalar";

        public int Order => 35;

        public Type ComponentType => typeof(Web.Pages.ContactCases);

        public string? Permission => CasesPermissions.Read;
    }
}
