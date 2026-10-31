using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Membership.Application;
using Arma.Modules.Membership.Infrastructure;
using Arma.Modules.Membership.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Membership;

public sealed class MembershipModule : IModule, INavigationContributor
{
    public string Name => "Üyelik";

    public Assembly PagesAssembly => typeof(MembershipModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => MembershipPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<MembershipDbContext>(configuration, MembershipDbContext.Schema);
        services.AddScoped<MembershipService>();
        services.AddScoped<IDataImporter, CivicrmMembershipImporter>();
        services.AddScoped<RenewalReminderJob>();
        services.AddScoped<IScheduledJob>(sp => sp.GetRequiredService<RenewalReminderJob>());
        services.AddScoped<IPersonalDataSource, MembershipPersonalData>();
        services.AddScoped<MembershipInsights>();
        services.AddScoped<IContactCriteriaProvider>(sp => sp.GetRequiredService<MembershipInsights>());
        services.AddScoped<IDashboardContributor>(sp => sp.GetRequiredService<MembershipInsights>());
        services.AddSingleton<IContactPanel, MembershipPanel>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => MembershipEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MembershipDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Üyeler", "memberships", 35, MembershipPermissions.Read),
        new("Aidatlar", "memberships/dues", 36, MembershipPermissions.Read),
        new("Üyelik türleri", "memberships/types", 37, MembershipPermissions.Read),
    ];

    private sealed class MembershipPanel : IContactPanel
    {
        public string Key => "membership";

        public string Title => "Üyelik";

        public int Order => 25;

        public Type ComponentType => typeof(Web.Pages.ContactMemberships);

        public string? Permission => MembershipPermissions.Read;
    }
}
