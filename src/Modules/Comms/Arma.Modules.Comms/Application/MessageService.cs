using Arma.Modules.Activities.Contracts;
using Arma.Modules.Comms.Domain;
using Arma.Modules.Comms.Infrastructure;
using Arma.Modules.Comms.Senders;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Comms.Application;

/// <summary>Kuyruktaki iletiyi gönderir.</summary>
public sealed record DeliverMessage(Guid MessageId);

public sealed record MessageView(Guid Id, Guid? ContactId, Guid? MailingId, Channel Channel, string? To, string? Subject, string Body, bool IsCommercial, MessageStatus Status, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, string? CreatedBy);

public sealed record SendToContactRequest(Guid ContactId, Channel Channel, string? Subject, string Body, bool IsCommercial);

public sealed class MessageService(
    CommsDbContext db,
    IContactAudiences audiences,
    ConsentService consents,
    IEmailSender email,
    ISmsSender sms,
    IDbContextOutbox outbox,
    IOptions<CommsOptions> options,
    TimeProvider clock)
{
    internal const string SourceModule = "Comms";

    public async Task<IReadOnlyList<MessageView>> ForContactAsync(Guid contactId, int take = 100, CancellationToken cancellationToken = default) =>
        await db.Messages.AsNoTracking().Where(m => m.ContactId == contactId)
            .OrderByDescending(m => m.CreatedAt).Take(take)
            .Select(m => new MessageView(m.Id, m.ContactId, m.MailingId, m.Channel, m.To, m.Subject, m.Body, m.IsCommercial, m.Status, m.Error, m.CreatedAt, m.SentAt, m.CreatedBy))
            .ToListAsync(cancellationToken);

    /// <summary>Bir kişiye tek ileti: kuyruğa alınır, arka planda gönderilir.</summary>
    public async Task<Result<Guid>> SendToContactAsync(SendToContactRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Channel is not (Channel.Email or Channel.Sms))
        {
            return new Error("messages.channel", "Yalnızca e-posta veya SMS gönderilebilir.");
        }

        if (string.IsNullOrWhiteSpace(request.Body) || (request.Channel == Channel.Email && string.IsNullOrWhiteSpace(request.Subject)))
        {
            return new Error("messages.required", request.Channel == Channel.Email ? "Konu ve metin gerekli." : "Metin gerekli.");
        }

        var contacts = await audiences.FindManyAsync([request.ContactId], cancellationToken);
        if (!contacts.TryGetValue(request.ContactId, out var contact))
        {
            return new Error("messages.contact_not_found", "Kişi bulunamadı.");
        }

        var to = AddressOf(contact, request.Channel);
        if (to is null)
        {
            return new Error("messages.no_address", request.Channel == Channel.Email ? "Kişinin e-posta adresi yok." : "Kişinin geçerli bir cep telefonu yok.");
        }

        if (request.IsCommercial && !(await consents.ApprovedAsync(request.Channel, [contact.Id], cancellationToken)).Contains(contact.Id))
        {
            return new Error("messages.no_consent", "Kişinin bu kanaldan ticari ileti izni yok.");
        }

        var message = OutboundMessage.Create(contact.Id, null, request.Channel, to,
            request.Subject is null ? null : MessageText.Merge(request.Subject, contact), MessageText.Merge(request.Body, contact), request.IsCommercial);
        db.Messages.Add(message);
        outbox.Enroll(db);
        await outbox.PublishAsync(new DeliverMessage(message.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return message.Id;
    }

    /// <summary>Başka bir modülün istediği bilgilendirme e-postası (makbuz, teşekkür).</summary>
    internal async Task QueueTransactionalAsync(Guid contactId, string subject, string body, CancellationToken cancellationToken)
    {
        var contacts = await audiences.FindManyAsync([contactId], cancellationToken);
        if (!contacts.TryGetValue(contactId, out var contact))
        {
            return;
        }

        var message = OutboundMessage.Create(contactId, null, Channel.Email, contact.Email, subject, MessageText.Merge(body, contact), false);
        db.Messages.Add(message);
        if (contact.Email is null)
        {
            message.Skip("E-posta adresi yok");
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        outbox.Enroll(db);
        await outbox.PublishAsync(new DeliverMessage(message.Id));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }

    /// <summary>
    /// İletiyi gönderir. Ticari iletide izin gönderim anında yeniden kontrol edilir (kuyruktayken iptal edilmiş olabilir).
    /// Aynı ileti tekrar gelirse yeniden gönderilmez.
    /// </summary>
    internal async Task DeliverAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var message = await db.Messages.FirstOrDefaultAsync(m => m.Id == messageId, cancellationToken);
        if (message is null || message.Status != MessageStatus.Pending)
        {
            return;
        }

        if (message.IsCommercial && message.ContactId is { } contactId
            && !(await consents.ApprovedAsync(message.Channel, [contactId], cancellationToken)).Contains(contactId))
        {
            message.Skip("İleti izni yok");
        }
        else
        {
            var token = message.IsCommercial && message.ContactId is { } id ? consents.UnsubscribeToken(id, message.Channel) : null;
            var unsubscribe = PublicUrl("abonelik/iptal", token);
            var result = message.Channel == Channel.Email
                ? await email.SendAsync(new EmailEnvelope(message.To!, null, message.Subject ?? "", MessageText.WithUnsubscribeText(message.Body, unsubscribe),
                    MessageText.ToHtml(message.Body, unsubscribe), PublicUrl("api/v1/unsubscribe", token)), cancellationToken)
                : await sms.SendAsync(message.To!, message.Body, message.IsCommercial, cancellationToken);
            if (result.Success)
            {
                message.MarkSent(result.ProviderId, clock.GetUtcNow());
            }
            else
            {
                message.MarkFailed(result.Error ?? "Bilinmeyen hata");
            }
        }

        outbox.Enroll(db);
        if (message.Status == MessageStatus.Sent && message.ContactId is { } contact)
        {
            var kind = message.Channel == Channel.Email ? ActivityKind.Email : ActivityKind.Sms;
            var subject = message.Channel == Channel.Email ? $"E-posta: {message.Subject}" : $"SMS: {Shorten(message.Body, 80)}";
            await outbox.PublishAsync(new RecordTimelineEntry([contact], kind, subject, message.Channel == Channel.Email ? Shorten(message.Body, 1000) : message.Body,
                message.SentAt!.Value, SourceModule, message.Id.ToString()));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
    }

    internal static string? AddressOf(AudienceMember contact, Channel channel) =>
        channel == Channel.Email ? (string.IsNullOrWhiteSpace(contact.Email) ? null : contact.Email.Trim()) : MessageText.MobileNumber(contact.Phone);

    /// <summary>
    /// Ayarlardan yapılan deneme gönderimi: kuyruğa alınmaz, sonuç hemen döner.
    /// </summary>
    public Task<SendResult> SendTestEmailAsync(string to, CancellationToken cancellationToken = default) =>
        email.SendAsync(new EmailEnvelope(to.Trim(), null, "Arma CRM deneme e-postası", "E-posta ayarları çalışıyor.",
            MessageText.ToHtml("E-posta ayarları çalışıyor.", null), null), cancellationToken);

    public Task<SendResult> SendTestSmsAsync(string phone, CancellationToken cancellationToken = default) =>
        MessageText.MobileNumber(phone) is { } number
            ? sms.SendAsync(number, "Arma CRM deneme SMS'i.", false, cancellationToken)
            : Task.FromResult(SendResult.Fail("Geçerli bir cep telefonu girin (5XX XXX XX XX)."));

    private string? PublicUrl(string path, string? token) =>
        token is null || string.IsNullOrWhiteSpace(options.Value.PublicUrl)
            ? null
            : $"{options.Value.PublicUrl.TrimEnd('/')}/{path}?t={Uri.EscapeDataString(token)}";

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";
}
