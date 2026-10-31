using Arma.SharedKernel.Payments;
using Microsoft.Extensions.Logging;

namespace Arma.Payments.Pipeline;

internal static class HandlerOrder
{
    public const int Guard = -1000;
    public const int Resolution = 0;
    public const int Validation = 100;
    public const int Capability = 200;
}

internal static class PaymentMessages
{
    public const string Unavailable = "Bu ödeme yöntemi şu an kullanılamıyor.";
    public const string ProviderError = "Ödeme sistemine bağlanılamadı. Lütfen daha sonra tekrar deneyin.";
}

/// <summary>Sağlayıcıyı bulur ve ayarlarının tam olduğunu doğrular; değilse zinciri keser.</summary>
internal static class Resolution
{
    public static bool TryResolve(IPaymentProviderFactory factory, PaymentContext context, ILogger logger)
    {
        var handle = factory.Create(context.ProviderKey);
        if (handle is null)
        {
            logger.LogWarning("Bilinmeyen ödeme sağlayıcısı: {Provider}", context.ProviderKey);
            return false;
        }

        if (!handle.Settings.IsConfigured)
        {
            logger.LogWarning("Ödeme sağlayıcısı {Provider} yapılandırılmamış; eksik ayarlar: {Missing}", context.ProviderKey,
                string.Join(", ", handle.Settings.Missing.Select(m => m.Key)));
            return false;
        }

        (context.Provider, context.Settings) = (handle.Provider, handle.Settings);
        return true;
    }
}

internal sealed class StartGuardHandler(ILogger<StartGuardHandler> logger) : IPaymentHandler<PaymentStartContext, PaymentStart>
{
    public int Order => HandlerOrder.Guard;

    public async Task<PaymentStart> HandleAsync(PaymentStartContext context, Func<Task<PaymentStart>> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Ödeme başlatılamadı ({Provider}, {OrderId})", context.ProviderKey, context.Request.OrderId);
            return PaymentStart.Failed(PaymentMessages.ProviderError);
        }
    }
}

internal sealed class StartResolutionHandler(IPaymentProviderFactory factory, ILogger<StartResolutionHandler> logger) : IPaymentHandler<PaymentStartContext, PaymentStart>
{
    public int Order => HandlerOrder.Resolution;

    public Task<PaymentStart> HandleAsync(PaymentStartContext context, Func<Task<PaymentStart>> next, CancellationToken cancellationToken) =>
        Resolution.TryResolve(factory, context, logger) ? next() : Task.FromResult(PaymentStart.Failed(PaymentMessages.Unavailable));
}

/// <summary>Sağlayıcıya gitmeden önce isteğin tutarlı olduğunu denetler.</summary>
internal sealed class StartValidationHandler : IPaymentHandler<PaymentStartContext, PaymentStart>
{
    public int Order => HandlerOrder.Validation;

    public Task<PaymentStart> HandleAsync(PaymentStartContext context, Func<Task<PaymentStart>> next, CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (request.Amount <= 0 || decimal.Round(request.Amount, 2) != request.Amount)
        {
            return Task.FromResult(PaymentStart.Failed("Tutar geçersiz."));
        }

        return request.Currency is "TRY" ? next() : Task.FromResult(PaymentStart.Failed("Yalnızca Türk lirası ile ödeme alınabilir."));
    }
}

/// <summary>Kart saklama istenmişse sağlayıcının bunu desteklediğini denetler.</summary>
internal sealed class StartCapabilityHandler : IPaymentHandler<PaymentStartContext, PaymentStart>
{
    public int Order => HandlerOrder.Capability;

    public Task<PaymentStart> HandleAsync(PaymentStartContext context, Func<Task<PaymentStart>> next, CancellationToken cancellationToken) =>
        context.Request.StoreCard && context.Provider is not IStoredCardPaymentProvider
            ? Task.FromResult(PaymentStart.Failed("Bu ödeme yöntemiyle düzenli bağış yapılamıyor."))
            : next();
}

internal sealed class CallbackGuardHandler(ILogger<CallbackGuardHandler> logger) : IPaymentHandler<PaymentCallbackContext, PaymentNotification?>
{
    public int Order => HandlerOrder.Guard;

    public async Task<PaymentNotification?> HandleAsync(PaymentCallbackContext context, Func<Task<PaymentNotification?>> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next() is { } notification ? notification with { ProviderKey = context.ProviderKey } : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Ödeme bildirimi doğrulanamadı ({Provider})", context.ProviderKey);
            return null;
        }
    }
}

internal sealed class CallbackResolutionHandler(IPaymentProviderFactory factory, ILogger<CallbackResolutionHandler> logger) : IPaymentHandler<PaymentCallbackContext, PaymentNotification?>
{
    public int Order => HandlerOrder.Resolution;

    public Task<PaymentNotification?> HandleAsync(PaymentCallbackContext context, Func<Task<PaymentNotification?>> next, CancellationToken cancellationToken) =>
        Resolution.TryResolve(factory, context, logger) ? next() : Task.FromResult<PaymentNotification?>(null);
}

internal sealed class ChargeGuardHandler(ILogger<ChargeGuardHandler> logger) : IPaymentHandler<StoredCardChargeContext, PaymentResult>
{
    public int Order => HandlerOrder.Guard;

    public async Task<PaymentResult> HandleAsync(StoredCardChargeContext context, Func<Task<PaymentResult>> next, CancellationToken cancellationToken)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Kayıtlı karttan tahsilat yapılamadı ({Provider}, {OrderId})", context.ProviderKey, context.Charge.OrderId);
            return PaymentResult.Failed(PaymentMessages.ProviderError);
        }
    }
}

internal sealed class ChargeResolutionHandler(IPaymentProviderFactory factory, ILogger<ChargeResolutionHandler> logger) : IPaymentHandler<StoredCardChargeContext, PaymentResult>
{
    public int Order => HandlerOrder.Resolution;

    public Task<PaymentResult> HandleAsync(StoredCardChargeContext context, Func<Task<PaymentResult>> next, CancellationToken cancellationToken)
    {
        if (!Resolution.TryResolve(factory, context, logger))
        {
            return Task.FromResult(PaymentResult.Failed(PaymentMessages.Unavailable));
        }

        return context.Provider is IStoredCardPaymentProvider
            ? next()
            : Task.FromResult(PaymentResult.Failed("Bu ödeme yöntemi kayıtlı karttan tahsilatı desteklemiyor."));
    }
}

internal sealed class ChargeValidationHandler : IPaymentHandler<StoredCardChargeContext, PaymentResult>
{
    public int Order => HandlerOrder.Validation;

    public Task<PaymentResult> HandleAsync(StoredCardChargeContext context, Func<Task<PaymentResult>> next, CancellationToken cancellationToken)
    {
        var charge = context.Charge;
        if (charge.Amount <= 0 || decimal.Round(charge.Amount, 2) != charge.Amount || !charge.OrderId.All(char.IsAsciiLetterOrDigit))
        {
            return Task.FromResult(PaymentResult.Failed("Tahsilat isteği geçersiz."));
        }

        return string.IsNullOrEmpty(charge.Card.Token) ? Task.FromResult(PaymentResult.Failed("Kayıtlı kart bulunamadı.")) : next();
    }
}
