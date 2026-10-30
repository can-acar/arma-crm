using Arma.Modules.Comms.Contracts;
using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Comms.Application;

/// <summary>İletişim modülünün kuyruk işleri ve dinlediği olaylar.</summary>
public static class CommsEventHandler
{
    public static async Task Handle(DeliverMessage message, MessageService messages, MailingService mailings, CancellationToken cancellationToken)
    {
        await messages.DeliverAsync(message.MessageId, cancellationToken);
        await mailings.CompleteIfDoneAsync(message.MessageId, cancellationToken);
    }

    public static Task Handle(ExpandMailing message, MailingService mailings, CancellationToken cancellationToken) =>
        mailings.ExpandAsync(message.MailingId, cancellationToken);

    public static Task Handle(SyncConsentToIys message, ConsentService consents, CancellationToken cancellationToken) =>
        consents.SyncAsync(message.ConsentId, cancellationToken);

    public static Task Handle(SendTransactionalEmail message, MessageService messages, CancellationToken cancellationToken) =>
        messages.QueueTransactionalAsync(message.ContactId, message.Subject, message.Body, cancellationToken);

    public static Task Handle(RecordChannelConsent message, ConsentService consents, CancellationToken cancellationToken)
    {
        Domain.Channel? channel = message.Channel switch
        {
            "email" => Domain.Channel.Email,
            "sms" => Domain.Channel.Sms,
            "call" => Domain.Channel.Call,
            _ => null,
        };
        return channel is { } c
            ? consents.SetAsync(message.ContactId, c, message.Approved ? Domain.ConsentStatus.Approved : Domain.ConsentStatus.Rejected, message.Source, cancellationToken)
            : Task.CompletedTask;
    }

    public static Task Handle(ContactsMerged message, ConsentService consents, CancellationToken cancellationToken) =>
        consents.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
