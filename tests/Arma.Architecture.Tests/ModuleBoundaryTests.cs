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
    private static readonly Assembly ContactsContracts = typeof(IContactDirectory).Assembly;

    [Fact]
    public void Shared_kernel_does_not_depend_on_modules_or_host() =>
        Types.InAssembly(SharedKernel).ShouldNot().HaveDependencyOnAny("Arma.Modules", "Arma.Host", "Arma.UI")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Ui_library_does_not_depend_on_business_code() =>
        Types.InAssembly(Ui).ShouldNot().HaveDependencyOnAny("Arma.Modules", "Arma.Host", "Arma.SharedKernel")
            .GetResult().IsSuccessful.ShouldBeTrue();

    [Fact]
    public void Contracts_do_not_leak_persistence_types() =>
        Types.InAssembly(ContactsContracts).ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Arma.Modules.Contacts.Domain", "Arma.Modules.Contacts.Infrastructure")
            .GetResult().IsSuccessful.ShouldBeTrue();

    public static TheoryData<string> ModuleAssemblies => new(Modules.Keys);

    private static readonly Dictionary<string, Assembly> Modules = new()
    {
        ["Arma.Modules.Audit"] = typeof(Arma.Modules.Audit.AuditModule).Assembly,
        ["Arma.Modules.Contacts"] = typeof(Arma.Modules.Contacts.ContactsModule).Assembly,
        ["Arma.Modules.Identity"] = typeof(Arma.Modules.Identity.IdentityModule).Assembly,
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
