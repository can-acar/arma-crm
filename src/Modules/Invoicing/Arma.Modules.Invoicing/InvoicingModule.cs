using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Invoicing.Application;
using Arma.Modules.Invoicing.Infrastructure;
using Arma.Modules.Invoicing.Pipeline;
using Arma.Modules.Invoicing.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Privacy;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Invoicing;

/// <summary>
/// e-Fatura ve e-Arşiv: kabul edilen teklif ve alınan aidattan fatura, GİB numaralaması, UBL-TR belgesi ve eklenebilir
/// entegratörler. Entegratörler <see cref="SharedKernel.Invoicing.IEInvoiceProvider"/> uygulayan sınıflardır; eklentilerdekiler
/// kendiliğinden bulunur.
/// </summary>
public sealed class InvoicingModule : IModule, INavigationContributor
{
    public string Name => "Faturalar";

    public Assembly PagesAssembly => typeof(InvoicingModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => InvoicingPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<InvoicingDbContext>(configuration, InvoicingDbContext.Schema);
        services.AddArmaEInvoicing();
        services.AddScoped<InvoiceService>();
        services.AddScoped<IPersonalDataSource, InvoicingPersonalData>();
        services.AddSingleton<IContactPanel, InvoicesPanel>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => InvoicingEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<InvoicingDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Faturalar", "invoices", 27, InvoicingPermissions.Read),
        new("Fatura ayarları", "admin/invoicing", 946, InvoicingPermissions.Settings),
    ];

    private sealed class InvoicesPanel : IContactPanel
    {
        public string Key => "invoices";

        public string Title => "Faturalar";

        public int Order => 32;

        public Type ComponentType => typeof(Web.Pages.ContactInvoices);

        public string? Permission => InvoicingPermissions.Read;
    }
}
