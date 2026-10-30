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

    [Fact]
    public void Contacts_module_does_not_depend_on_host() =>
        Types.InAssembly(typeof(Arma.Modules.Contacts.ContactsModule).Assembly).ShouldNot().HaveDependencyOn("Arma.Host")
            .GetResult().IsSuccessful.ShouldBeTrue();
}
