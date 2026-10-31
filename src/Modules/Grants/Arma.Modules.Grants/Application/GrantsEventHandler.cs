using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Grants.Application;

/// <summary>Hibe modülünün dinlediği modüller arası olaylar.</summary>
public static class GrantsEventHandler
{
    public static Task Handle(ContactsMerged message, GrantService service, CancellationToken cancellationToken) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
