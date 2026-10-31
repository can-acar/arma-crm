using Arma.Payments.Pipeline;
using Arma.SharedKernel.Payments;
using Microsoft.AspNetCore.Http;

namespace Arma.Payments;

internal sealed class PaymentGateway(
    IPaymentProviderFactory factory,
    IEnumerable<IPaymentHandler<PaymentStartContext, PaymentStart>> startHandlers,
    IEnumerable<IPaymentHandler<PaymentCallbackContext, PaymentNotification?>> callbackHandlers,
    IEnumerable<IPaymentHandler<StoredCardChargeContext, PaymentResult>> chargeHandlers) : IPaymentGateway
{
    public IReadOnlyList<PaymentProviderInfo> Providers =>
        [.. factory.All.Select(h => new PaymentProviderInfo(h.Descriptor, h.Settings.Missing))];

    public PaymentProviderInfo? Find(string providerKey) =>
        factory.Create(providerKey) is { } h ? new PaymentProviderInfo(h.Descriptor, h.Settings.Missing) : null;

    public Task<PaymentStart> StartAsync(PaymentRequest request, CancellationToken cancellationToken = default) =>
        PaymentChain.RunAsync(startHandlers, new PaymentStartContext(request),
            (c, ct) => c.Provider!.StartAsync(c.Request, c.Settings!, ct), cancellationToken);

    public Task<PaymentNotification?> VerifyCallbackAsync(string providerKey, IFormCollection form, CancellationToken cancellationToken = default) =>
        PaymentChain.RunAsync(callbackHandlers, new PaymentCallbackContext(providerKey, form),
            (c, ct) => c.Provider!.VerifyCallbackAsync(c.Form, c.Settings!, ct), cancellationToken);

    public Task<PaymentResult> ChargeStoredCardAsync(StoredCardCharge charge, CancellationToken cancellationToken = default) =>
        PaymentChain.RunAsync(chargeHandlers, new StoredCardChargeContext(charge),
            (c, ct) => ((IStoredCardPaymentProvider)c.Provider!).ChargeAsync(c.Charge, c.Settings!, ct), cancellationToken);

    public IResult Acknowledge(string providerKey, PaymentNotification? notification, string successUrl, string failUrl) =>
        factory.Create(providerKey)?.Provider.Acknowledge(notification, successUrl, failUrl) ?? Results.NotFound();

    public bool AllowsFrame(string providerKey, string url) =>
        factory.Create(providerKey)?.Provider.AllowsFrame(url) == true;
}
