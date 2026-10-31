using System.Reflection;
using Arma.Modules.Invoicing.Providers;
using Arma.SharedKernel.Invoicing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Arma.Modules.Invoicing.Pipeline;

/// <summary>DI'a kaydedilmiş bir entegratör tipi.</summary>
public sealed record EInvoiceProviderRegistration(Type ProviderType);

/// <summary>Bir entegratör ve ayarları.</summary>
public sealed record EInvoiceProviderHandle(IEInvoiceProvider Provider, IEInvoiceProviderSettings Settings)
{
    public EInvoiceProviderDescriptor Descriptor => Provider.Descriptor;
}

/// <summary>Entegratörleri anahtarla bulup ayarlarıyla birlikte üretir (fabrika).</summary>
public interface IEInvoiceProviderFactory
{
    IReadOnlyList<EInvoiceProviderHandle> All { get; }

    EInvoiceProviderHandle? Create(string providerKey);
}

internal sealed class EInvoiceProviderFactory : IEInvoiceProviderFactory
{
    public const string Section = "EInvoicing:Providers";

    private readonly Dictionary<string, EInvoiceProviderHandle> handles = new(StringComparer.OrdinalIgnoreCase);

    public EInvoiceProviderFactory(IServiceProvider services, IEnumerable<EInvoiceProviderRegistration> registrations, IConfiguration configuration, IHostEnvironment environment)
    {
        foreach (var registration in registrations.DistinctBy(r => r.ProviderType))
        {
            var provider = (IEInvoiceProvider)services.GetRequiredService(registration.ProviderType);
            var descriptor = provider.Descriptor;
            if (descriptor.DevelopmentOnly && environment.IsProduction())
            {
                continue;
            }

            if (descriptor.Key is not { Length: > 0 and <= 40 } key || !key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'))
            {
                throw new InvalidOperationException($"e-Fatura entegratörü {registration.ProviderType.FullName} geçersiz anahtar kullanıyor: '{descriptor.Key}'. Yalnızca küçük harf, rakam ve tire kullanılabilir.");
            }

            if (!handles.TryAdd(key, new EInvoiceProviderHandle(provider, new ConfigurationEInvoiceSettings(descriptor, configuration.GetSection($"{Section}:{key}")))))
            {
                throw new InvalidOperationException($"'{key}' anahtarlı iki e-Fatura entegratörü var: {handles[key].Provider.GetType().FullName} ve {registration.ProviderType.FullName}.");
            }
        }

        All = [.. handles.Values.OrderBy(h => h.Descriptor.Order).ThenBy(h => h.Descriptor.DisplayName, StringComparer.CurrentCulture)];
    }

    public IReadOnlyList<EInvoiceProviderHandle> All { get; }

    public EInvoiceProviderHandle? Create(string providerKey) => handles.GetValueOrDefault(providerKey ?? "");
}

/// <summary>Entegratör ayarlarını yapılandırmadan (ortam değişkeni, ayar dosyası, user-secrets) okur.</summary>
internal sealed class ConfigurationEInvoiceSettings(EInvoiceProviderDescriptor descriptor, IConfigurationSection section) : IEInvoiceProviderSettings
{
    public string ProviderKey => descriptor.Key;

    public string this[string field] =>
        section[field] is { Length: > 0 } value ? value.Trim() : descriptor.Settings.FirstOrDefault(s => s.Key == field)?.DefaultValue ?? "";

    public IReadOnlyList<EInvoiceSettingField> Missing => [.. descriptor.Settings.Where(s => s.IsRequired && this[s.Key].Length == 0)];
}

/// <summary>
/// e-Fatura altyapısını DI'a kurar (kurucu):
/// <code>
/// services.AddArmaEInvoicing()
///     .AddProvidersFrom(moduleAssemblies)   // eklentilerdeki entegratörler kendiliğinden bulunur
///     .AddHandler&lt;MyApprovalHandler&gt;();
/// </code>
/// </summary>
public sealed class EInvoicingBuilder
{
    public const string HttpClientName = "Arma.EInvoicing";

    internal EInvoicingBuilder(IServiceCollection services) => Services = services;

    public IServiceCollection Services { get; }

    public EInvoicingBuilder AddProvider<TProvider>() where TProvider : class, IEInvoiceProvider => AddProvider(typeof(TProvider));

    public EInvoicingBuilder AddProvider(Type providerType)
    {
        if (!IsProvider(providerType))
        {
            throw new ArgumentException($"{providerType.FullName} bir e-Fatura entegratörü değil.", nameof(providerType));
        }

        if (Services.Any(d => d.ImplementationInstance is EInvoiceProviderRegistration r && r.ProviderType == providerType))
        {
            return this;
        }

        Services.TryAddSingleton(providerType);
        Services.AddSingleton(new EInvoiceProviderRegistration(providerType));
        return this;
    }

    /// <summary>Derlemelerdeki bütün somut <see cref="IEInvoiceProvider"/> sınıflarını kaydeder.</summary>
    public EInvoicingBuilder AddProvidersFrom(params IEnumerable<Assembly> assemblies)
    {
        foreach (var type in assemblies.Distinct().SelectMany(LoadableTypes).Where(IsProvider).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            AddProvider(type);
        }

        return this;
    }

    public EInvoicingBuilder AddHandler<THandler>() where THandler : class, IEInvoiceHandler
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IEInvoiceHandler, THandler>());
        return this;
    }

    private static bool IsProvider(Type type) =>
        type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } && typeof(IEInvoiceProvider).IsAssignableFrom(type)
        && type.GetCustomAttribute<ExcludeFromEInvoiceDiscoveryAttribute>() is null;

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}

/// <summary>Entegratör sınıfının otomatik bulunmasını engeller.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExcludeFromEInvoiceDiscoveryAttribute : Attribute;

public static class EInvoicingServiceCollectionExtensions
{
    /// <summary>e-Fatura altyapısını ve yerleşik sağlayıcıları (GİB portalı dosyası, test) kaydeder. Birden çok çağrılabilir.</summary>
    public static EInvoicingBuilder AddArmaEInvoicing(this IServiceCollection services)
    {
        services.AddHttpClient(EInvoicingBuilder.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(60));
        services.TryAddSingleton<IEInvoiceProviderFactory, EInvoiceProviderFactory>();
        services.TryAddScoped<IEInvoiceGateway, EInvoiceGateway>();

        return new EInvoicingBuilder(services)
            .AddHandler<GuardHandler>()
            .AddHandler<ResolutionHandler>()
            .AddHandler<ValidationHandler>()
            .AddHandler<ProfileHandler>()
            .AddProvidersFrom(typeof(GibPortalFileProvider).Assembly);
    }
}

/// <summary>Zinciri kurar; son halka entegratörün kendisidir.</summary>
internal sealed class EInvoiceGateway(IEInvoiceProviderFactory factory, IEnumerable<IEInvoiceHandler> handlers) : IEInvoiceGateway
{
    public IReadOnlyList<EInvoiceProviderInfo> Providers => [.. factory.All.Select(h => new EInvoiceProviderInfo(h.Descriptor, h.Settings.Missing))];

    public EInvoiceProviderInfo? Find(string providerKey) =>
        factory.Create(providerKey) is { } h ? new EInvoiceProviderInfo(h.Descriptor, h.Settings.Missing) : null;

    public Task<EInvoiceSendResult> SendAsync(string providerKey, EInvoiceDocument document, CancellationToken cancellationToken = default)
    {
        var context = new EInvoiceSendContext(providerKey, document);
        Func<Task<EInvoiceSendResult>> next = () => context.Provider!.SendAsync(context.Document, context.Settings!, cancellationToken);
        foreach (var handler in handlers.OrderByDescending(h => h.Order))
        {
            var following = next;
            next = () => handler.HandleAsync(context, following, cancellationToken);
        }

        return next();
    }
}

/// <summary>Entegratörden gelen beklenmeyen hatayı kullanıcıya gösterilebilir sonuca çevirir.</summary>
internal sealed class GuardHandler(Microsoft.Extensions.Logging.ILogger<GuardHandler> logger) : IEInvoiceHandler
{
    public int Order => -1000;

    public async Task<EInvoiceSendResult> HandleAsync(EInvoiceSendContext context, Func<Task<EInvoiceSendResult>> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, ex, "{Number} numaralı fatura {Provider} ile gönderilemedi.", context.Document.Number, context.ProviderKey);
            return EInvoiceSendResult.Failed("Entegratöre bağlanılamadı. Biraz sonra yeniden deneyin; sorun sürerse sistem kayıtlarına bakın.");
        }
    }
}

internal sealed class ResolutionHandler(IEInvoiceProviderFactory factory) : IEInvoiceHandler
{
    public int Order => 0;

    public Task<EInvoiceSendResult> HandleAsync(EInvoiceSendContext context, Func<Task<EInvoiceSendResult>> next, CancellationToken cancellationToken)
    {
        if (factory.Create(context.ProviderKey) is not { } handle)
        {
            return Task.FromResult(EInvoiceSendResult.Failed("Seçilen e-Fatura entegratörü bu kurulumda yok."));
        }

        if (!handle.Settings.IsConfigured)
        {
            return Task.FromResult(EInvoiceSendResult.Failed($"{handle.Descriptor.DisplayName} ayarları eksik: {string.Join(", ", handle.Settings.Missing.Select(m => m.Label))}."));
        }

        (context.Provider, context.Settings) = (handle.Provider, handle.Settings);
        return next();
    }
}

/// <summary>Tutarların ve belgenin tutarlılığını son kez denetler.</summary>
internal sealed class ValidationHandler : IEInvoiceHandler
{
    public int Order => 100;

    public Task<EInvoiceSendResult> HandleAsync(EInvoiceSendContext context, Func<Task<EInvoiceSendResult>> next, CancellationToken cancellationToken)
    {
        var d = context.Document;
        if (d.Xml.Length == 0 || d.Payable <= 0 || d.Lines.Count == 0)
        {
            return Task.FromResult(EInvoiceSendResult.Failed("Fatura belgesi eksik veya tutarı sıfır."));
        }

        return next();
    }
}

/// <summary>Entegratörün faturanın senaryosunu (e-Arşiv, e-Fatura) destekleyip desteklemediğine bakar.</summary>
internal sealed class ProfileHandler : IEInvoiceHandler
{
    public int Order => 200;

    public Task<EInvoiceSendResult> HandleAsync(EInvoiceSendContext context, Func<Task<EInvoiceSendResult>> next, CancellationToken cancellationToken) =>
        context.Provider!.Descriptor.Supports(context.Document.Profile)
            ? next()
            : Task.FromResult(EInvoiceSendResult.Failed($"{context.Provider.Descriptor.DisplayName} {(context.Document.Profile == EInvoiceProfiles.EFatura ? "e-Fatura" : "e-Arşiv")} gönderemiyor."));
}
