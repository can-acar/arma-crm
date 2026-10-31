using System.Reflection;
using Arma.Modules.Activities;
using Arma.Modules.Audit;
using Arma.Modules.Contacts;
using Arma.Modules.Events;
using Arma.Modules.Sales;
using Arma.Modules.Automation;
using Arma.Modules.Campaigns;
using Arma.Modules.Cases;
using Arma.Modules.Grants;
using Arma.Modules.Webhooks;
using Arma.Modules.Portal;
using Arma.Modules.Forms;
using Arma.Modules.Comms;
using Arma.Modules.Giving;
using Arma.Modules.Identity;
using Arma.Modules.Invoicing;
using Arma.Modules.Membership;
using Arma.Modules.Privacy;
using Arma.Infrastructure.Plugins;
using Arma.Host.Plugins;
using Arma.SharedKernel.Modules;

namespace Arma.Host;

/// <summary>Bu kurulumda etkin olan modüller: yerleşik modüller ve plugins klasöründen yüklenen eklentiler.</summary>
public static class ArmaModules
{
    private static readonly Lock Gate = new();
    private static bool pluginsLoaded;

    /// <summary>Yerleşik modüller. Yeni modül buraya eklenir.</summary>
    public static IReadOnlyList<IModule> BuiltIn { get; } = [new IdentityModule(), new AuditModule(), new ContactsModule(), new ActivitiesModule(), new GivingModule(), new CommsModule(), new PrivacyModule(), new FormsModule(), new MembershipModule(), new EventsModule(), new SalesModule(), new InvoicingModule(), new AutomationModule(), new CampaignsModule(), new CasesModule(), new GrantsModule(), new WebhooksModule(), new PortalModule(), new PluginsModule()];

    public static IReadOnlyList<IModule> All { get; private set; } = BuiltIn;

    public static IReadOnlyList<PluginInfo> Plugins { get; private set; } = [];

    public static Assembly[] ModuleAssemblies => All.Select(m => m.GetType().Assembly).Distinct().ToArray();

    public static Assembly[] PageAssemblies => All.Select(m => m.PagesAssembly).OfType<Assembly>().Distinct().ToArray();

    public static Version ArmaVersion { get; } = typeof(ArmaModules).Assembly.GetName().Version ?? new Version(1, 0);

    /// <summary>
    /// Eklentileri <c>Arma:Plugins:Path</c> klasöründen (varsayılan: uygulama klasöründeki plugins) bir kez yükler.
    /// <c>Arma:Plugins:Disabled</c> listesindeki kimlikler atlanır; <c>Arma:Plugins:Enabled=false</c> hepsini kapatır.
    /// </summary>
    public static void LoadPlugins(IConfiguration configuration, string contentRoot)
    {
        lock (Gate)
        {
            // Testlerde aynı süreçte birden çok uygulama açılır; eklentiler bir kez yüklenir.
            if (pluginsLoaded || !configuration.GetValue("Arma:Plugins:Enabled", true))
            {
                return;
            }

            pluginsLoaded = true;

            var root = configuration["Arma:Plugins:Path"] is { Length: > 0 } path ? Path.GetFullPath(path, contentRoot) : Path.Combine(contentRoot, "plugins");
            var disabled = configuration.GetSection("Arma:Plugins:Disabled").Get<string[]>() ?? [];
            Plugins = PluginLoader.LoadAll(root, disabled, ArmaVersion, [.. BuiltIn.Select(m => m.Name)]);
            All = [.. BuiltIn, .. Plugins.SelectMany(p => p.Modules)];
        }
    }
}
