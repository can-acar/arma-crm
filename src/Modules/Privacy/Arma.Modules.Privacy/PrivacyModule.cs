using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Privacy.Application;
using Arma.Modules.Privacy.Infrastructure;
using Arma.Modules.Privacy.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Privacy;

public sealed class PrivacyModule : IModule, INavigationContributor
{
    public string Name => "KVKK";

    public Assembly PagesAssembly => typeof(PrivacyModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => PrivacyPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<PrivacyDbContext>(configuration, PrivacyDbContext.Schema);
        services.AddScoped<NoticeService>();
        services.AddScoped<ConsentRecordService>();
        services.AddScoped<DataRequestService>();
        services.AddScoped<PersonalDataService>();
        services.AddScoped<IPersonalDataSource, PrivacyPersonalData>();
        services.AddScoped<PrivacyInsights>();
        services.AddScoped<IContactCriteriaProvider>(sp => sp.GetRequiredService<PrivacyInsights>());
        services.AddScoped<IDashboardContributor>(sp => sp.GetRequiredService<PrivacyInsights>());
        services.AddSingleton<IContactPanel, PrivacyPanel>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PrivacyEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PrivacyDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("KVKK başvuruları", "privacy/requests", 950, PrivacyPermissions.Read),
        new("Aydınlatma metinleri", "privacy/notices", 951, PrivacyPermissions.Read),
        new("Kişisel veri envanteri", "privacy/inventory", 952, PrivacyPermissions.Read),
    ];

    private sealed class PrivacyPanel : IContactPanel
    {
        public string Key => "privacy";

        public string Title => "KVKK";

        public int Order => 90;

        public Type ComponentType => typeof(Web.Pages.ContactPrivacy);

        public string? Permission => PrivacyPermissions.Read;
    }
}
