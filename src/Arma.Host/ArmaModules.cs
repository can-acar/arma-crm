using System.Reflection;
using Arma.Modules.Contacts;
using Arma.Modules.Identity;
using Arma.SharedKernel.Modules;

namespace Arma.Host;

/// <summary>Bu kurulumda etkin olan modüller. Yeni modül buraya eklenir.</summary>
public static class ArmaModules
{
    public static IReadOnlyList<IModule> All { get; } = [new IdentityModule(), new ContactsModule()];

    public static Assembly[] PageAssemblies { get; } =
        All.Select(m => m.PagesAssembly).OfType<Assembly>().Distinct().ToArray();
}
