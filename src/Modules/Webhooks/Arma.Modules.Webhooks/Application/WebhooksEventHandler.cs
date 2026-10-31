using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Webhooks.Application;

/// <summary>Webhook modülünün dinlediği modüller arası olaylar.</summary>
public static class WebhooksEventHandler
{
    public static Task Handle(RecordTimelineEntry message, WebhookService service, CancellationToken cancellationToken) =>
        service.OnTimelineAsync(message, cancellationToken);

    public static Task Handle(ContactsMerged message, WebhookService service, CancellationToken cancellationToken) =>
        service.OnMergedAsync(message, cancellationToken);
}
