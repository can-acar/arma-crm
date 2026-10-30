using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Infrastructure;
using Arma.Modules.Contacts.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Contacts;

public sealed class ContactsModule : IModule, INavigationContributor
{
    public string Name => "Kişiler";

    public Assembly PagesAssembly => typeof(ContactsModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => ContactsPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ContactsDbContext>(configuration, ContactsDbContext.Schema);

        services.AddScoped<ContactService>();
        services.AddScoped<IContactDirectory>(sp => sp.GetRequiredService<ContactService>());
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ContactEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContactsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() => [new NavigationItem("Kişiler", "contacts", 10, ContactsPermissions.Read)];
}
