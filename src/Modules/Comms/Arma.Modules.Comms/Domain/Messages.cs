using Arma.SharedKernel.Domain;

namespace Arma.Modules.Comms.Domain;

public enum Channel
{
    Email = 1,
    Sms = 2,
    Call = 3,
}

public enum ConsentStatus
{
    Unknown = 0,
    Approved = 1,
    Rejected = 2,
}

public enum IysState
{
    /// <summary>İYS kapalı veya kanal İYS kapsamında değil.</summary>
    NotRequired = 0,
    Pending = 1,
    Synced = 2,
    Failed = 3,
}

/// <summary>
/// Kişinin bir kanaldan ticari ileti almaya izni (6563 sayılı Kanun, İYS). Kanal başına tek kayıt;
/// her değişiklik denetim izinde saklanır.
/// </summary>
public sealed class ChannelConsent : Entity
{
    private ChannelConsent()
    {
    }

    public Guid ContactId { get; private set; }

    public Channel Channel { get; private set; }

    public ConsentStatus Status { get; private set; }

    /// <summary>İznin alındığı adres (e-posta veya telefon); İYS'ye bu adres bildirilir.</summary>
    public string? Address { get; private set; }

    /// <summary>İznin kaynağı: form, elle, içe aktarma, abonelik iptali, İYS.</summary>
    public string Source { get; private set; } = "";

    public DateTimeOffset RecordedAt { get; private set; }

    public IysState IysState { get; private set; }

    public string? IysError { get; private set; }

    public static ChannelConsent Create(Guid contactId, Channel channel) =>
        new() { ContactId = contactId, Channel = channel };

    public void Set(ConsentStatus status, string? address, string source, DateTimeOffset now, bool iysEnabled)
    {
        Status = status;
        Address = string.IsNullOrWhiteSpace(address) ? Address : address.Trim();
        Source = source;
        RecordedAt = now;
        IysState = iysEnabled && status != ConsentStatus.Unknown && Address is not null ? IysState.Pending : IysState.NotRequired;
        IysError = null;
    }

    public void MarkIys(bool success, string? error)
    {
        IysState = success ? IysState.Synced : IysState.Failed;
        IysError = success ? null : error?[..Math.Min(error.Length, 500)];
    }

    public void ChangeContact(Guid contactId) => ContactId = contactId;
}

public enum MessageStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,

    /// <summary>Adres yok veya ileti izni yok; gönderilmedi.</summary>
    Skipped = 4,
}

/// <summary>Gönderilen (veya gönderilmeye çalışılan) tek e-posta ya da SMS.</summary>
public sealed class OutboundMessage : Entity
{
    private OutboundMessage()
    {
    }

    public Guid? ContactId { get; private set; }

    public Guid? MailingId { get; private set; }

    public Channel Channel { get; private set; }

    public string? To { get; private set; }

    public string? Subject { get; private set; }

    public string Body { get; private set; } = "";

    public bool IsCommercial { get; private set; }

    public MessageStatus Status { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Gönderim servisinin iş numarası.</summary>
    public string? ProviderMessageId { get; private set; }

    public static OutboundMessage Create(Guid? contactId, Guid? mailingId, Channel channel, string? to, string? subject, string body, bool isCommercial) =>
        new()
        {
            ContactId = contactId,
            MailingId = mailingId,
            Channel = channel,
            To = string.IsNullOrWhiteSpace(to) ? null : to.Trim(),
            Subject = subject,
            Body = body,
            IsCommercial = isCommercial,
            Status = MessageStatus.Pending,
        };

    public void MarkSent(string? providerId, DateTimeOffset now)
    {
        Status = MessageStatus.Sent;
        SentAt = now;
        ProviderMessageId = providerId;
        Error = null;
    }

    public void MarkFailed(string error)
    {
        Status = MessageStatus.Failed;
        Error = error[..Math.Min(error.Length, 1000)];
    }

    public void Skip(string reason)
    {
        Status = MessageStatus.Skipped;
        Error = reason;
    }

    public void ChangeContact(Guid contactId) => ContactId = contactId;
}

public enum MailingStatus
{
    Draft = 1,
    Sending = 2,
    Completed = 3,
}

/// <summary>Bir gruba veya etikete gönderilen toplu e-posta ya da SMS.</summary>
public sealed class Mailing : Entity
{
    private Mailing()
    {
    }

    public string Name { get; private set; } = "";

    public Channel Channel { get; private set; }

    public string? Subject { get; private set; }

    public string Body { get; private set; } = "";

    public bool IsCommercial { get; private set; }

    public Guid? GroupId { get; private set; }

    public Guid? TagId { get; private set; }

    public MailingStatus Status { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static Result<Mailing> Create(string name, Channel channel, string? subject, string body, bool isCommercial, Guid? groupId, Guid? tagId)
    {
        var mailing = new Mailing { Status = MailingStatus.Draft };
        var result = mailing.Update(name, channel, subject, body, isCommercial, groupId, tagId);
        return result.IsSuccess ? mailing : result.Error!;
    }

    public Result<bool> Update(string name, Channel channel, string? subject, string body, bool isCommercial, Guid? groupId, Guid? tagId)
    {
        if (Status != MailingStatus.Draft)
        {
            return new Error("mailings.not_draft", "Gönderimi başlamış ileti değiştirilemez.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return new Error("mailings.name", "Ad gerekli.");
        }

        if (channel is not (Channel.Email or Channel.Sms))
        {
            return new Error("mailings.channel", "Toplu gönderim yalnızca e-posta veya SMS ile yapılır.");
        }

        if (channel == Channel.Email && string.IsNullOrWhiteSpace(subject))
        {
            return new Error("mailings.subject", "E-posta konusu gerekli.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new Error("mailings.body", "İleti metni gerekli.");
        }

        Name = name.Trim();
        Channel = channel;
        Subject = channel == Channel.Email ? subject!.Trim() : null;
        Body = body.Trim();
        IsCommercial = isCommercial;
        GroupId = groupId;
        TagId = tagId;
        return true;
    }

    public Result<bool> Start(DateTimeOffset now)
    {
        if (Status != MailingStatus.Draft)
        {
            return new Error("mailings.not_draft", "Bu ileti zaten gönderildi.");
        }

        Status = MailingStatus.Sending;
        StartedAt = now;
        return true;
    }

    public void Complete(DateTimeOffset now)
    {
        Status = MailingStatus.Completed;
        CompletedAt = now;
    }
}

/// <summary>Toplu iletide ve tek gönderimde kullanılabilen hazır metin.</summary>
public sealed class MessageTemplate : Entity
{
    private MessageTemplate()
    {
    }

    public string Name { get; private set; } = "";

    public Channel Channel { get; private set; }

    public string? Subject { get; private set; }

    public string Body { get; private set; } = "";

    public static Result<MessageTemplate> Create(string name, Channel channel, string? subject, string body)
    {
        var template = new MessageTemplate();
        var result = template.Update(name, channel, subject, body);
        return result.IsSuccess ? template : result.Error!;
    }

    public Result<bool> Update(string name, Channel channel, string? subject, string body)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body))
        {
            return new Error("templates.required", "Ad ve metin gerekli.");
        }

        Name = name.Trim();
        Channel = channel;
        Subject = channel == Channel.Email ? subject?.Trim() : null;
        Body = body.Trim();
        return true;
    }
}
