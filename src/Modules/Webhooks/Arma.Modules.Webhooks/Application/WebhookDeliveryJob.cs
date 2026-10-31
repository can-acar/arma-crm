using Arma.SharedKernel.Modules;

namespace Arma.Modules.Webhooks.Application;

/// <summary>Kuyruktaki webhook gönderimlerini yapar.</summary>
internal sealed class WebhookDeliveryJob(WebhookService service) : IScheduledJob
{
    public string Name => "webhooks.deliver";

    public TimeSpan Interval => TimeSpan.FromMinutes(1);

    public Task RunAsync(CancellationToken cancellationToken) => service.DeliverDueAsync(cancellationToken);
}
