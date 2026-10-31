using Arma.SharedKernel.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arma.Payments;

/// <summary>DI'a kaydedilmiş bir sağlayıcı tipi.</summary>
public sealed record PaymentProviderRegistration(Type ProviderType);

/// <summary>Bir sağlayıcı ve o sağlayıcının ayarları; zincir ve ağ geçidi bununla çalışır.</summary>
public sealed record PaymentProviderHandle(IPaymentProvider Provider, IPaymentProviderSettings Settings)
{
    public PaymentProviderDescriptor Descriptor => Provider.Descriptor;
}

/// <summary>Sağlayıcıları anahtarla bulup ayarlarıyla birlikte üretir (fabrika).</summary>
public interface IPaymentProviderFactory
{
    /// <summary>Bu ortamda kullanılabilen bütün sağlayıcılar, sıralı.</summary>
    IReadOnlyList<PaymentProviderHandle> All { get; }

    /// <summary>Anahtarı bilinen sağlayıcı; yoksa veya bu ortamda kullanılamıyorsa null.</summary>
    PaymentProviderHandle? Create(string providerKey);
}

internal sealed class PaymentProviderFactory : IPaymentProviderFactory
{
    public const string Section = "Payments:Providers";

    private readonly Dictionary<string, PaymentProviderHandle> handles;

    public PaymentProviderFactory(IServiceProvider services, IEnumerable<PaymentProviderRegistration> registrations, IConfiguration configuration, IHostEnvironment environment)
    {
        handles = new(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations.DistinctBy(r => r.ProviderType))
        {
            var provider = (IPaymentProvider)services.GetRequiredService(registration.ProviderType);
            var descriptor = provider.Descriptor;
            if (descriptor.DevelopmentOnly && environment.IsProduction())
            {
                continue;
            }

            if (!IsValidKey(descriptor.Key))
            {
                throw new InvalidOperationException($"Ödeme sağlayıcısı {registration.ProviderType.FullName} geçersiz anahtar kullanıyor: '{descriptor.Key}'. Yalnızca küçük harf, rakam ve tire kullanılabilir.");
            }

            if (!handles.TryAdd(descriptor.Key, new PaymentProviderHandle(provider, new ConfigurationPaymentSettings(descriptor, configuration.GetSection($"{Section}:{descriptor.Key}")))))
            {
                throw new InvalidOperationException($"'{descriptor.Key}' anahtarlı iki ödeme sağlayıcısı var: {handles[descriptor.Key].Provider.GetType().FullName} ve {registration.ProviderType.FullName}.");
            }
        }

        All = [.. handles.Values.OrderBy(h => h.Descriptor.Order).ThenBy(h => h.Descriptor.DisplayName, StringComparer.CurrentCulture)];
    }

    public IReadOnlyList<PaymentProviderHandle> All { get; }

    public PaymentProviderHandle? Create(string providerKey) => handles.GetValueOrDefault(providerKey ?? "");

    internal static bool IsValidKey(string key) =>
        key is { Length: > 0 and <= 40 } && key.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
}

/// <summary>Sağlayıcı ayarlarını yapılandırmadan (ortam değişkeni, ayar dosyası, user-secrets) okur.</summary>
internal sealed class ConfigurationPaymentSettings(PaymentProviderDescriptor descriptor, IConfigurationSection section) : IPaymentProviderSettings
{
    public string ProviderKey => descriptor.Key;

    public string this[string field] =>
        section[field] is { Length: > 0 } value ? value.Trim() : descriptor.Settings.FirstOrDefault(s => s.Key == field)?.DefaultValue ?? "";

    public IReadOnlyList<PaymentSettingField> Missing => [.. descriptor.Settings.Where(s => s.IsRequired && this[s.Key].Length == 0)];
}
