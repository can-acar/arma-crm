using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Infrastructure;
using Arma.Modules.Contacts.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
        services.AddScoped<IContactRegistry>(sp => sp.GetRequiredService<ContactService>());
        services.AddScoped<IContactBillingDirectory>(sp => sp.GetRequiredService<ContactService>());
        services.AddScoped<TaxonomyService>();
        services.AddScoped<IContactAudiences, ContactAudiences>();
        services.AddScoped<IContactSegments>(sp => sp.GetRequiredService<ContactSearchService>());
        services.AddScoped<ContactTransferService>();
        services.AddScoped<ContactSearchService>();
        services.AddScoped<CustomFieldService>();
        services.AddScoped<IDataImporter, CivicrmContactImporter>();
        services.AddScoped<IContactCriteriaProvider>(sp => sp.GetRequiredService<CustomFieldService>());
        services.AddScoped<IDashboardContributor, ContactsDashboard>();
        services.AddScoped<IPersonalDataSource, ContactsPersonalData>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ContactEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContactsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var encrypted = await NationalIdEncryption.EncryptPlaintextAsync(db, scope.ServiceProvider.GetRequiredService<ISensitiveDataProtector>(), cancellationToken);
        if (encrypted > 0)
        {
            scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<ContactsModule>()
                .LogInformation("{Count} kişinin kimlik numarası şifrelendi", encrypted);
        }
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Kişiler", "contacts", 10, ContactsPermissions.Read),
        new("Gelişmiş arama", "contacts/search", 11, ContactsPermissions.Read),
        new("Etiket ve gruplar", "contacts/groups", 12, ContactsPermissions.Read),
        new("Kişi ek alanları", "admin/custom-fields", 925, ContactsPermissions.Fields),
        new("Veri aktarımı", "admin/import", 927, ContactsPermissions.Import),
    ];
}
