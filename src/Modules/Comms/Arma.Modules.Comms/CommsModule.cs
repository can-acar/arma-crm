using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Comms.Application;
using Arma.Modules.Comms.Infrastructure;
using Arma.Modules.Comms.Senders;
using Arma.Modules.Comms.Web;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arma.Modules.Comms;

public sealed class CommsModule : IModule, INavigationContributor
{
    public string Name => "İletişim";

    public Assembly PagesAssembly => typeof(CommsModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => CommsPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<CommsDbContext>(configuration, CommsDbContext.Schema);
        services.AddScoped<ConsentService>();
        services.AddScoped<MessageService>();
        services.AddScoped<MailingService>();
        services.AddScoped<Arma.SharedKernel.Privacy.IPersonalDataSource, CommsPersonalData>();
        services.AddSingleton<IContactPanel, CommsPanel>();
        services.AddSingleton<INavigationContributor>(this);
        AddSenders(services, configuration);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CommsEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CommsDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Toplu ileti", "comms/mailings", 40, CommsPermissions.Mailings),
        new("İleti şablonları", "comms/templates", 41, CommsPermissions.Mailings),
        new("E-posta ve SMS", "admin/comms", 940, CommsPermissions.Settings),
    ];

    private static void AddSenders(IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(CommsOptions.Section);
        services.Configure<CommsOptions>(section);
        var production = string.Equals(configuration[HostDefaults.EnvironmentKey], "Production", StringComparison.OrdinalIgnoreCase);
        var email = Normalize(section["Email:Provider"]);
        var sms = Normalize(section["Sms:Provider"]);
        if (production && (email == "test" || sms == "test"))
        {
            throw new InvalidOperationException("Test e-posta/SMS sağlayıcısı canlı ortamda kullanılamaz.");
        }

        if (email == "test" || sms == "test")
        {
            services.AddSingleton<TestMessageSink>();
        }

        switch (email)
        {
            case "smtp":
                services.AddSingleton<IEmailSender, SmtpEmailSender>();
                break;
            case "test":
                services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<TestMessageSink>());
                break;
            case "none":
                services.AddSingleton<IEmailSender, DisabledSender>();
                break;
            default:
                throw new InvalidOperationException($"Bilinmeyen e-posta sağlayıcısı: '{email}'. Geçerli değerler: none, smtp.");
        }

        switch (sms)
        {
            case "netgsm":
                services.AddHttpClient<ISmsSender, NetgsmSmsSender>(c => c.Timeout = TimeSpan.FromSeconds(30));
                break;
            case "test":
                services.AddSingleton<ISmsSender>(sp => sp.GetRequiredService<TestMessageSink>());
                break;
            case "none":
                services.AddSingleton<ISmsSender, DisabledSender>();
                break;
            default:
                throw new InvalidOperationException($"Bilinmeyen SMS sağlayıcısı: '{sms}'. Geçerli değerler: none, netgsm.");
        }

        if (section.GetValue<bool>("Iys:Enabled"))
        {
            services.AddHttpClient<IIysClient, IysClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        }
        else
        {
            services.AddSingleton<IIysClient, DisabledIysClient>();
        }
    }

    private static string Normalize(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? "none" : provider.Trim().ToLowerInvariant();

    private sealed class CommsPanel : IContactPanel
    {
        public string Key => "comms";

        public string Title => "İletişim";

        public int Order => 30;

        public Type ComponentType => typeof(Web.Pages.ContactComms);

        public string? Permission => CommsPermissions.Read;
    }
}
