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
using Arma.Modules.Forms;
using Arma.Modules.Comms;
using Arma.Modules.Giving;
using Arma.Modules.Identity;
using Arma.Modules.Membership;
using Arma.Modules.Privacy;
using Arma.SharedKernel.Modules;

namespace Arma.Host;

/// <summary>Bu kurulumda etkin olan modüller. Yeni modül buraya eklenir.</summary>
public static class ArmaModules
{
    public static IReadOnlyList<IModule> All { get; } = [new IdentityModule(), new AuditModule(), new ContactsModule(), new ActivitiesModule(), new GivingModule(), new CommsModule(), new PrivacyModule(), new FormsModule(), new MembershipModule(), new EventsModule(), new SalesModule(), new AutomationModule(), new CampaignsModule(), new CasesModule(), new GrantsModule(), new WebhooksModule()];

    public static Assembly[] ModuleAssemblies { get; } = All.Select(m => m.GetType().Assembly).Distinct().ToArray();

    public static Assembly[] PageAssemblies { get; } =
        All.Select(m => m.PagesAssembly).OfType<Assembly>().Distinct().ToArray();
}
