using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Membership.Application;

/// <summary>Üyelik modülünün dinlediği modüller arası olaylar.</summary>
public static class MembershipEventHandler
{
    public static Task Handle(ContactsMerged message, MembershipService service, CancellationToken cancellationToken) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
