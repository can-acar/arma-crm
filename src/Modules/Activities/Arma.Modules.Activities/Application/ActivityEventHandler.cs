using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Activities.Application;

/// <summary>Modüller arası olaylar. Wolverine bu sınıfı bulur ve mesajları kalıcı kuyruktan işler.</summary>
public static class ActivityEventHandler
{
    public static Task Handle(ContactsMerged message, ActivityService service, CancellationToken cancellationToken) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);

    public static Task Handle(RecordTimelineEntry message, ActivityService service, CancellationToken cancellationToken) =>
        service.RecordAsync(message, cancellationToken);

    public static Task Handle(RemoveTimelineEntry message, ActivityService service, CancellationToken cancellationToken) =>
        service.RemoveSourceAsync(message.SourceModule, message.SourceId, cancellationToken);

    public static Task Handle(CreateFollowUpTask message, ActivityService service, CancellationToken cancellationToken) =>
        service.CreateFollowUpAsync(message, cancellationToken);
}
