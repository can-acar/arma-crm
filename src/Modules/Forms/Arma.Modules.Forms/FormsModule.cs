using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Forms.Application;
using Arma.Modules.Forms.Infrastructure;
using Arma.Modules.Forms.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Forms;

public sealed class FormsModule : IModule, INavigationContributor
{
    public string Name => "Formlar";

    public Assembly PagesAssembly => typeof(FormsModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => FormsPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<FormsDbContext>(configuration, FormsDbContext.Schema);
        services.AddScoped<FormService>();
        services.AddScoped<IPersonalDataSource, FormsPersonalData>();
        services.AddScoped<ICampaignSource, FormCampaignSource>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => FormsEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FormsDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Formlar", "forms", 45, FormsPermissions.Read),
    ];
}
