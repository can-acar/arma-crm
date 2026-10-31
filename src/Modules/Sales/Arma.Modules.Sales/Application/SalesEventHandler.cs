using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Sales.Application;

/// <summary>Satış modülünün dinlediği modüller arası olaylar.</summary>
public static class SalesEventHandler
{
    public static Task Handle(ContactsMerged message, SalesService service, CancellationToken cancellationToken) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
