using System.Reflection;
using Arma.Payments.Pipeline;
using Arma.Payments.Providers;
using Arma.SharedKernel.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arma.Payments;

/// <summary>
/// Ödeme altyapısını DI'a kurar (kurucu):
/// <code>
/// services.AddArmaPayments()
///     .AddProvidersFrom(moduleAssemblies)   // eklentilerdeki sağlayıcılar kendiliğinden bulunur
///     .AddProvider&lt;MyProvider&gt;()          // ya da tek tek eklenir
///     .AddHandler&lt;PaymentStartContext, PaymentStart, MyFraudCheck&gt;();
/// </code>
/// </summary>
public sealed class PaymentsBuilder
{
    public const string HttpClientName = "Arma.Payments";

    internal PaymentsBuilder(IServiceCollection services) => Services = services;

    public IServiceCollection Services { get; }

    public PaymentsBuilder AddProvider<TProvider>() where TProvider : class, IPaymentProvider => AddProvider(typeof(TProvider));

    public PaymentsBuilder AddProvider(Type providerType)
    {
        if (!IsProvider(providerType))
        {
            throw new ArgumentException($"{providerType.FullName} bir ödeme sağlayıcısı değil.", nameof(providerType));
        }

        if (Services.Any(d => d.ServiceType == typeof(PaymentProviderRegistration) && d.ImplementationInstance is PaymentProviderRegistration r && r.ProviderType == providerType))
        {
            return this;
        }

        Services.TryAddSingleton(providerType);
        Services.AddSingleton(new PaymentProviderRegistration(providerType));
        return this;
    }

    /// <summary>Derlemelerdeki bütün somut <see cref="IPaymentProvider"/> sınıflarını kaydeder.</summary>
    public PaymentsBuilder AddProvidersFrom(params IEnumerable<Assembly> assemblies)
    {
        foreach (var type in assemblies.Distinct().SelectMany(LoadableTypes).Where(IsProvider).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            AddProvider(type);
        }

        return this;
    }

    public PaymentsBuilder AddHandler<TContext, TResult, THandler>()
        where TContext : PaymentContext
        where THandler : class, IPaymentHandler<TContext, TResult>
    {
        Services.TryAddEnumerable(ServiceDescriptor.Scoped<IPaymentHandler<TContext, TResult>, THandler>());
        return this;
    }

    private static bool IsProvider(Type type) =>
        type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } && typeof(IPaymentProvider).IsAssignableFrom(type)
        && type.GetCustomAttribute<ExcludeFromDiscoveryAttribute>() is null;

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

/// <summary>Sağlayıcı sınıfının otomatik bulunmasını engeller (ör. yalnızca testte elle eklenen sağlayıcılar).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExcludeFromDiscoveryAttribute : Attribute;

public static class PaymentServiceCollectionExtensions
{
    /// <summary>Ödeme altyapısını ve yerleşik sağlayıcıları (iyzico, PayTR, test) kaydeder.</summary>
    public static PaymentsBuilder AddArmaPayments(this IServiceCollection services)
    {
        services.AddHttpClient(PaymentsBuilder.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(30));
        services.TryAddSingleton<IPaymentProviderFactory, PaymentProviderFactory>();
        services.TryAddScoped<IPaymentGateway, PaymentGateway>();

        var builder = new PaymentsBuilder(services)
            .AddHandler<PaymentStartContext, PaymentStart, StartGuardHandler>()
            .AddHandler<PaymentStartContext, PaymentStart, StartResolutionHandler>()
            .AddHandler<PaymentStartContext, PaymentStart, StartValidationHandler>()
            .AddHandler<PaymentStartContext, PaymentStart, StartCapabilityHandler>()
            .AddHandler<PaymentCallbackContext, PaymentNotification?, CallbackGuardHandler>()
            .AddHandler<PaymentCallbackContext, PaymentNotification?, CallbackResolutionHandler>()
            .AddHandler<StoredCardChargeContext, PaymentResult, ChargeGuardHandler>()
            .AddHandler<StoredCardChargeContext, PaymentResult, ChargeResolutionHandler>()
            .AddHandler<StoredCardChargeContext, PaymentResult, ChargeValidationHandler>();

        return builder.AddProvidersFrom(typeof(IyzicoProvider).Assembly);
    }
}
