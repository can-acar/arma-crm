using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Automation.Application;

/// <summary>Otomasyon modülünün dinlediği modüller arası olaylar.</summary>
public static class AutomationEventHandler
{
    public static Task Handle(RecordTimelineEntry message, AutomationService service, CancellationToken cancellationToken) =>
        service.OnTimelineAsync(message, cancellationToken);

    public static Task Handle(ContactsMerged message, AutomationService service, CancellationToken cancellationToken) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
