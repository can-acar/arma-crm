using Arma.Modules.Giving.Application.Banking;
using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Giving.Application;

/// <summary>Bağışlar modülünün dinlediği modüller arası olaylar.</summary>
public static class GivingEventHandler
{
    public static async Task Handle(ContactsMerged message, DonationService service, BankService bank, CancellationToken cancellationToken)
    {
        await service.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
        await bank.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
    }
}
