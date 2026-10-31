using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Cases.Application;

/// <summary>Vaka modülünün dinlediği modüller arası olaylar.</summary>
public static class CasesEventHandler
{
    public static Task Handle(ContactsMerged message, CaseService service, CancellationToken cancellationToken) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
