using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arma.SharedKernel.Modules;

namespace Arma.Infrastructure.Plugins;

/// <summary>Eklenti klasöründeki <c>plugin.json</c> dosyası.</summary>
/// <param name="Id">Kalıcı, küçük harfli kimlik ("kurum.dogum-gunu"); devre dışı bırakırken bu yazılır.</param>
/// <param name="Assembly">Klasördeki ana derlemenin dosya adı ("Kurum.DogumGunu.dll").</param>
/// <param name="MinimumArmaVersion">Eklentinin çalıştığı en eski Arma sürümü ("1.0").</param>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string Assembly,
    string? Author = null,
    string? Description = null,
    string? Website = null,
    string? MinimumArmaVersion = null);

public enum PluginState
{
    Loaded = 1,
    Disabled = 2,
    Failed = 3,
}

/// <summary>Bir eklenti klasörünün yükleme sonucu. Hatalı eklenti uygulamayı durdurmaz, yönetim ekranında görünür.</summary>
public sealed record PluginInfo(string Folder, PluginManifest? Manifest, PluginState State, string? Error, IReadOnlyList<IModule> Modules)
{
    public string Id => Manifest?.Id ?? Path.GetFileName(Folder);
}

/// <summary>
/// Eklentileri yükler. Her eklenti kendi klasöründedir; klasörde <c>plugin.json</c> ve ana derleme bulunur.
/// Arma'nın ve .NET'in derlemeleri uygulamayla paylaşılır, eklentinin kendi bağımlılıkları klasöründen yüklenir.
/// </summary>
public static partial class PluginLoader
{
    public const string ManifestFile = "plugin.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static IReadOnlyList<PluginInfo> LoadAll(string root, IReadOnlyCollection<string> disabled, Version armaVersion, IReadOnlyCollection<string> reservedModuleNames)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        var results = new List<PluginInfo>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moduleNames = new HashSet<string>(reservedModuleNames, StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.GetDirectories(root).Order(StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(folder, ManifestFile)))
            {
                continue;
            }

            var result = Load(folder, disabled, armaVersion, ids);
            if (result.State == PluginState.Loaded && result.Modules.FirstOrDefault(m => !moduleNames.Add(m.Name)) is { } clash)
            {
                result = result with { State = PluginState.Failed, Error = $"\"{clash.Name}\" adlı bir modül zaten var.", Modules = [] };
            }

            results.Add(result);
        }

        return results;
    }

    private static PluginInfo Load(string folder, IReadOnlyCollection<string> disabled, Version armaVersion, HashSet<string> ids)
    {
        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(folder, ManifestFile)), Json);
        }
        catch (JsonException e)
        {
            return Failed(folder, null, $"{ManifestFile} okunamadı: {e.Message}");
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Name) || string.IsNullOrWhiteSpace(manifest.Version))
        {
            return Failed(folder, manifest, $"{ManifestFile} içinde id, name, version ve assembly zorunludur.");
        }

        if (manifest.Id is null || !IdPattern().IsMatch(manifest.Id))
        {
            return Failed(folder, manifest, "Kimlik (id) küçük harf, rakam, nokta ve tireden oluşmalı; 2-64 karakter.");
        }

        if (!ids.Add(manifest.Id))
        {
            return Failed(folder, manifest, $"\"{manifest.Id}\" kimlikli başka bir eklenti zaten yüklü.");
        }

        if (disabled.Contains(manifest.Id, StringComparer.OrdinalIgnoreCase))
        {
            return new PluginInfo(folder, manifest, PluginState.Disabled, null, []);
        }

        if (manifest.MinimumArmaVersion is { } minimum && (!System.Version.TryParse(minimum, out var required) || required > armaVersion))
        {
            return Failed(folder, manifest, $"Bu eklenti Arma {minimum} veya üstünü gerektiriyor (kurulu sürüm {armaVersion.ToString(2)}).");
        }

        if (string.IsNullOrWhiteSpace(manifest.Assembly) || manifest.Assembly != Path.GetFileName(manifest.Assembly)
            || !manifest.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return Failed(folder, manifest, "assembly, klasördeki .dll dosyasının adı olmalı.");
        }

        var path = Path.Combine(folder, manifest.Assembly);
        if (!File.Exists(path))
        {
            return Failed(folder, manifest, $"{manifest.Assembly} bulunamadı.");
        }

        try
        {
            var assembly = LoadIntoDefaultContext(Path.GetFullPath(path));
            var modules = assembly.GetExportedTypes()
                .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(t) && t.GetConstructor(Type.EmptyTypes) is not null)
                .Select(t => (IModule)Activator.CreateInstance(t)!)
                .ToList();
            return modules.Count == 0
                ? Failed(folder, manifest, $"{manifest.Assembly} içinde IModule uygulayan, parametresiz kurucusu olan bir sınıf yok.")
                : new PluginInfo(folder, manifest, PluginState.Loaded, null, modules);
        }
        catch (ReflectionTypeLoadException e)
        {
            var reason = e.LoaderExceptions.FirstOrDefault(x => x is not null)?.Message ?? e.Message;
            return Failed(folder, manifest, $"Eklenti bu Arma sürümüyle uyumsuz: {reason}");
        }
        catch (Exception e) when (e is BadImageFormatException or FileLoadException or FileNotFoundException or TypeLoadException or MissingMethodException or TargetInvocationException)
        {
            return Failed(folder, manifest, $"Eklenti yüklenemedi: {e.InnerException?.Message ?? e.Message}");
        }
    }

    private static PluginInfo Failed(string folder, PluginManifest? manifest, string error) => new(folder, manifest, PluginState.Failed, error, []);

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{1,63}$")]
    private static partial Regex IdPattern();

    /// <summary>
    /// Eklenti uygulamanın kendi yükleme bağlamına yüklenir: Blazor yönlendirici, Wolverine'in ürettiği işleyiciler ve EF Core
    /// tipleri adla çözer, ayrı bağlamdaki bir derlemeyi göremez. Uygulamada zaten olan derlemeler (Arma.*, ASP.NET Core,
    /// EF Core, Wolverine...) her zaman uygulamanınkidir; eklentinin yalnızca kendine ait bağımlılıkları klasöründen yüklenir.
    /// </summary>
    private static Assembly LoadIntoDefaultContext(string mainAssemblyPath)
    {
        var name = AssemblyName.GetAssemblyName(mainAssemblyPath);
        if (AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), name)) is { } loaded)
        {
            return loaded;
        }

        var resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        AssemblyLoadContext.Default.Resolving += (context, requested) =>
            resolver.ResolveAssemblyToPath(requested) is { } path ? context.LoadFromAssemblyPath(path) : null;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, dll) =>
            resolver.ResolveUnmanagedDllToPath(dll) is { } path ? System.Runtime.InteropServices.NativeLibrary.Load(path) : IntPtr.Zero;
        return AssemblyLoadContext.Default.LoadFromAssemblyPath(mainAssemblyPath);
    }
}
