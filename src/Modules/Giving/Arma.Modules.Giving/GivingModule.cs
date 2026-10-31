using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Infrastructure;
using Arma.Modules.Giving.Payments;
using Arma.Modules.Giving.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Giving;

public sealed class GivingModule : IModule, INavigationContributor
{
    public string Name => "Bağışlar";

    public Assembly PagesAssembly => typeof(GivingModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => GivingPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<GivingDbContext>(configuration, GivingDbContext.Schema);
        services.AddScoped<DonationService>();
        services.AddScoped<FundService>();
        services.AddScoped<PledgeService>();
        services.AddScoped<Arma.SharedKernel.Modules.IDataImporter, CivicrmContributionImporter>();
        services.AddScoped<OnlineGivingService>();
        services.AddScoped<Arma.Modules.Giving.Application.Banking.BankService>();
        services.AddScoped<Arma.SharedKernel.Privacy.IPersonalDataSource, GivingPersonalData>();
        services.AddScoped<GivingInsights>();
        services.AddScoped<IContactCriteriaProvider>(sp => sp.GetRequiredService<GivingInsights>());
        services.AddScoped<IDashboardContributor>(sp => sp.GetRequiredService<GivingInsights>());
        services.AddSingleton<IContactPanel, DonationsPanel>();
        services.AddSingleton<Arma.SharedKernel.Portal.IPortalSection, DonationsPortalSection>();
        services.AddScoped<ICampaignSource, FundCampaignSource>();
        services.AddSingleton<INavigationContributor>(this);
        AddPayments(services, configuration);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => GivingEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<GivingDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Bağışlar", "donations", 30, GivingPermissions.Read),
        new("Fonlar", "donations/funds", 31, GivingPermissions.Read),
        new("Taahhütler", "donations/pledges", 33, GivingPermissions.Read),
        new("Havale eşleştirme", "donations/bank", 32, GivingPermissions.Write),
        new("Makbuz ve ödeme", "admin/giving", 930, GivingPermissions.Settings),
    ];

    private static void AddPayments(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PaymentOptions.Section);
        services.Configure<PaymentOptions>(section);
        var provider = section["Provider"]?.Trim().ToLowerInvariant() ?? "none";
        switch (provider)
        {
            case "iyzico":
                services.AddHttpClient<IPaymentProvider, IyzicoProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
                break;
            case "paytr":
                services.AddHttpClient<IPaymentProvider, PayTrProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
                break;
            case "test":
                var environment = configuration[Microsoft.Extensions.Hosting.HostDefaults.EnvironmentKey];
                if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Test ödeme sağlayıcısı canlı ortamda kullanılamaz.");
                }

                services.AddScoped<IPaymentProvider, TestPaymentProvider>();
                break;
            case "none" or "":
                break;
            default:
                throw new InvalidOperationException($"Bilinmeyen ödeme sağlayıcısı: '{provider}'. Geçerli değerler: none, iyzico, paytr.");
        }
    }

    private sealed class DonationsPortalSection : Arma.SharedKernel.Portal.IPortalSection
    {
        public string Key => "donations";

        public string Title => "Bağışlarım";

        public int Order => 10;

        public Type ComponentType => typeof(Web.Pages.PortalDonations);
    }

    private sealed class DonationsPanel : IContactPanel
    {
        public string Key => "donations";

        public string Title => "Bağışlar";

        public int Order => 20;

        public Type ComponentType => typeof(Web.Pages.ContactDonations);

        public string? Permission => GivingPermissions.Read;
    }
}
