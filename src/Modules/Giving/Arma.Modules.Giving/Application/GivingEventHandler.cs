using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Giving.Application;

/// <summary>Bağışlar modülünün dinlediği modüller arası olaylar.</summary>
public static class GivingEventHandler
{
    public static Task Handle(ContactsMerged message, DonationService service, CancellationToken cancellationToken) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
