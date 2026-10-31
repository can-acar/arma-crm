using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Invoicing.Contracts;

namespace Arma.Modules.Invoicing.Application;

/// <summary>Fatura modülünün dinlediği modüller arası olaylar.</summary>
public static class InvoicingEventHandler
{
    public static Task Handle(InvoiceRequested message, InvoiceService service, CancellationToken cancellationToken) =>
        service.RequestAsync(message, cancellationToken);

    public static Task Handle(ContactsMerged message, InvoiceService service, CancellationToken cancellationToken) =>
        service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
