using System.Reflection;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Modules;
using Arma.UI;
using NetArchTest.Rules;

namespace Arma.Architecture.Tests;

/// <summary>Modüler monolitin sınırlarını koruyan kurallar.</summary>
public class ModuleBoundaryTests
{
    private static readonly Assembly SharedKernel = typeof(IModule).Assembly;
    private static readonly Assembly Ui = typeof(ClassNames).Assembly;
    public static TheoryData<string> ContractAssemblies => new(Contracts.Keys);

    private static readonly Dictionary<string, Assembly> Contracts = new()
    {
        ["Arma.Modules.Activities"] = typeof(Arma.Modules.Activities.Contracts.RecordTimelineEntry).Assembly,
        ["Arma.Modules.Comms"] = typeof(Arma.Modules.Comms.Contracts.SendTransactionalEmail).Assembly,
        ["Arma.Modules.Contacts"] = typeof(IContactDirectory).Assembly,
        ["Arma.Modules.Identity"] = typeof(Arma.Modules.Identity.Contracts.IUserDirectory).Assembly,
        ["Arma.Modules.Privacy"] = typeof(Arma.Modules.Privacy.Contracts.INoticeDirectory).Assembly,
    };

    [Fact]
    public void Shared_kernel_does_not_depend_on_modules_or_host() =>
        Types.InAssembly(SharedKernel).ShouldNot().HaveDependencyOnAny("Arma.Modules", "Arma.Host", "Arma.UI")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Ui_library_does_not_depend_on_business_code() =>
        Types.InAssembly(Ui).ShouldNot().HaveDependencyOnAny("Arma.Modules", "Arma.Host", "Arma.SharedKernel")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Theory]
    [MemberData(nameof(ContractAssemblies))]
    public void Contracts_do_not_leak_persistence_types(string module) =>
        Types.InAssembly(Contracts[module]).ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Wolverine", $"{module}.Domain", $"{module}.Infrastructure")
            .GetResult().IsSuccessful.ShouldBeTrue();

    public static TheoryData<string> ModuleAssemblies => new(Modules.Keys);

    private static readonly Dictionary<string, Assembly> Modules = new()
    {
        ["Arma.Modules.Activities"] = typeof(Arma.Modules.Activities.ActivitiesModule).Assembly,
        ["Arma.Modules.Audit"] = typeof(Arma.Modules.Audit.AuditModule).Assembly,
        ["Arma.Modules.Comms"] = typeof(Arma.Modules.Comms.CommsModule).Assembly,
        ["Arma.Modules.Contacts"] = typeof(Arma.Modules.Contacts.ContactsModule).Assembly,
        ["Arma.Modules.Events"] = typeof(Arma.Modules.Events.EventsModule).Assembly,
        ["Arma.Modules.Sales"] = typeof(Arma.Modules.Sales.SalesModule).Assembly,
        ["Arma.Modules.Automation"] = typeof(Arma.Modules.Automation.AutomationModule).Assembly,
        ["Arma.Modules.Campaigns"] = typeof(Arma.Modules.Campaigns.CampaignsModule).Assembly,
        ["Arma.Modules.Cases"] = typeof(Arma.Modules.Cases.CasesModule).Assembly,
        ["Arma.Modules.Grants"] = typeof(Arma.Modules.Grants.GrantsModule).Assembly,
        ["Arma.Modules.Forms"] = typeof(Arma.Modules.Forms.FormsModule).Assembly,
        ["Arma.Modules.Giving"] = typeof(Arma.Modules.Giving.GivingModule).Assembly,
        ["Arma.Modules.Identity"] = typeof(Arma.Modules.Identity.IdentityModule).Assembly,
        ["Arma.Modules.Membership"] = typeof(Arma.Modules.Membership.MembershipModule).Assembly,
        ["Arma.Modules.Privacy"] = typeof(Arma.Modules.Privacy.PrivacyModule).Assembly,
    };

    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Modules_do_not_depend_on_host(string module) =>
        Types.InAssembly(Modules[module]).ShouldNot().HaveDependencyOn("Arma.Host")
            .GetResult().IsSuccessful.ShouldBeTrue();

    /// <summary>Bir modül başka bir modüle yalnızca onun Contracts paketi üzerinden bağlanabilir.</summary>
    [Theory]
    [MemberData(nameof(ModuleAssemblies))]
    public void Modules_only_use_other_modules_through_contracts(string module)
    {
        string[] internals = [.. Modules.Keys.Where(m => m != module)
            .SelectMany(m => new[] { $"{m}.Domain", $"{m}.Application", $"{m}.Infrastructure", $"{m}.Web" })];

        Types.InAssembly(Modules[module]).ShouldNot().HaveDependencyOnAny(internals)
            .GetResult().IsSuccessful.ShouldBeTrue();
    }
}
