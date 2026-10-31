using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Events.Application;

/// <summary>Etkinlik modülünün dinlediği modüller arası olaylar.</summary>
public static class EventsEventHandler
{
    public static Task Handle(ContactsMerged message, EventService service, CancellationToken cancellationToken) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
