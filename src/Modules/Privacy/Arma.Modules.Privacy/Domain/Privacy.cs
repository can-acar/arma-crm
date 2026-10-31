using System.Text.RegularExpressions;
using Arma.SharedKernel.Domain;

namespace Arma.Modules.Privacy.Domain;

/// <summary>
/// Aydınlatma veya açık rıza metni. Taslak serbestçe düzenlenir; yayımlanan her sürüm
/// <see cref="NoticeVersion"/> olarak değişmeden saklanır, rıza kayıtları o sürüme bağlanır.
/// </summary>
public sealed partial class Notice : Entity
{
    private Notice()
    {
    }

    public string Title { get; private set; } = "";

    /// <summary>Kamuya açık adresin son parçası: /kvkk/{Slug}.</summary>
    public string Slug { get; private set; } = "";

    /// <summary>Açık rıza alınan metin mi (pazarlama, yurt dışına aktarım) yoksa yalnızca aydınlatma mı.</summary>
    public bool RequiresConsent { get; private set; }

    public string DraftText { get; private set; } = "";

    public int CurrentVersion { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public bool HasUnpublishedChanges { get; private set; } = true;

    public static Result<Notice> Create(string title, string slug, bool requiresConsent, string text)
    {
        var notice = new Notice();
        var updated = notice.Update(title, slug, requiresConsent, text);
        return updated.IsSuccess ? notice : updated.Error!;
    }

    public Result<bool> Update(string title, string slug, bool requiresConsent, string text)
    {
        slug = (slug ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(text))
        {
            return new Error("notices.required", "Başlık ve metin gerekli.");
        }

        if (!SlugPattern().IsMatch(slug))
        {
            return new Error("notices.slug", "Adres yalnızca küçük harf, rakam ve tire içerebilir (örnek: bagis-aydinlatma).");
        }

        if (slug == "basvuru")
        {
            return new Error("notices.slug_reserved", "Bu adres başvuru formuna ayrılmıştır.");
        }

        if (CurrentVersion > 0 && slug != Slug)
        {
            return new Error("notices.slug_locked", "Yayımlanmış metnin adresi değiştirilemez.");
        }

        HasUnpublishedChanges |= title.Trim() != Title || text.Trim() != DraftText || requiresConsent != RequiresConsent;
        (Title, Slug, RequiresConsent, DraftText) = (title.Trim(), slug, requiresConsent, text.Trim());
        return true;
    }

    public Result<NoticeVersion> Publish(DateTimeOffset now)
    {
        if (!HasUnpublishedChanges)
        {
            return new Error("notices.no_changes", "Yayımlanmış sürümden farklı bir değişiklik yok.");
        }

        CurrentVersion++;
        PublishedAt = now;
        HasUnpublishedChanges = false;
        return new NoticeVersion(Id, CurrentVersion, Title, DraftText, RequiresConsent, now);
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

/// <summary>Yayımlanmış metnin değişmez kopyası.</summary>
public sealed class NoticeVersion : Entity
{
    private NoticeVersion()
    {
    }

    internal NoticeVersion(Guid noticeId, int number, string title, string text, bool requiresConsent, DateTimeOffset publishedAt) =>
        (NoticeId, Number, Title, Text, RequiresConsent, PublishedAt) = (noticeId, number, title, text, requiresConsent, publishedAt);

    public Guid NoticeId { get; private set; }

    public int Number { get; private set; }

    public string Title { get; private set; } = "";

    public string Text { get; private set; } = "";

    public bool RequiresConsent { get; private set; }

    public DateTimeOffset PublishedAt { get; private set; }
}

/// <summary>
/// Açık rıza verildi veya geri alındı. Kayıtlar yalnızca eklenir; kişinin güncel durumu metin için son kayıttır.
/// </summary>
public sealed class ConsentRecord : Entity
{
    private ConsentRecord()
    {
    }

    public Guid ContactId { get; private set; }

    public Guid NoticeId { get; private set; }

    public Guid NoticeVersionId { get; private set; }

    public int VersionNumber { get; private set; }

    public bool Granted { get; private set; }

    /// <summary>manual, form, api.</summary>
    public string Source { get; private set; } = "";

    public string? IpAddress { get; private set; }

    public string? Note { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public static ConsentRecord Create(Guid contactId, NoticeVersion version, bool granted, string source, string? ipAddress, string? note, DateTimeOffset now) => new()
    {
        ContactId = contactId,
        NoticeId = version.NoticeId,
        NoticeVersionId = version.Id,
        VersionNumber = version.Number,
        Granted = granted,
        Source = source,
        IpAddress = ipAddress,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        RecordedAt = now,
    };
}

public enum RequestType
{
    /// <summary>Kişisel verimin işlenip işlenmediğini öğrenmek (KVKK m.11/a-b).</summary>
    Information = 1,

    /// <summary>İşlenen verilerin bir kopyası.</summary>
    Access = 2,

    Correction = 3,

    Erasure = 4,

    /// <summary>İşlemeye veya otomatik sonuca itiraz.</summary>
    Objection = 5,

    Other = 9,
}

public enum RequestStatus
{
    New = 1,
    InProgress = 2,
    Completed = 3,
    Rejected = 4,
}

public enum RequestChannel
{
    Web = 1,
    Email = 2,
    InPerson = 3,
    Mail = 4,
}

/// <summary>İlgili kişi başvurusu. Kanun gereği en geç 30 gün içinde yanıtlanır.</summary>
public sealed class DataRequest : Entity
{
    public const int ResponseDays = 30;

    private DataRequest()
    {
    }

    public RequestType Type { get; private set; }

    public RequestChannel Channel { get; private set; }

    public string RequesterName { get; private set; } = "";

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    public string Description { get; private set; } = "";

    public Guid? ContactId { get; private set; }

    public DateOnly ReceivedOn { get; private set; }

    public DateOnly DueOn { get; private set; }

    public RequestStatus Status { get; private set; }

    /// <summary>Başvuranın kimliği (kimlik kartı, e-imza, kayıtlı e-posta) doğrulandı mı.</summary>
    public bool IdentityVerified { get; private set; }

    public string? Response { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public bool IsOpen => Status is RequestStatus.New or RequestStatus.InProgress;

    public static Result<DataRequest> Create(RequestType type, RequestChannel channel, string name, string? email, string? phone, string description, DateOnly receivedOn)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description))
        {
            return new Error("requests.required", "Ad soyad ve başvuru metni gerekli.");
        }

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone) && channel == RequestChannel.Web)
        {
            return new Error("requests.contact_required", "Yanıt verebilmemiz için e-posta veya telefon gerekli.");
        }

        return new DataRequest
        {
            Type = type,
            Channel = channel,
            RequesterName = name.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
            Description = description.Trim(),
            ReceivedOn = receivedOn,
            DueOn = receivedOn.AddDays(ResponseDays),
            Status = RequestStatus.New,
        };
    }

    public void LinkContact(Guid? contactId) => ContactId = contactId;

    public void SetIdentityVerified(bool verified) => IdentityVerified = verified;

    public Result<bool> Start()
    {
        if (!IsOpen)
        {
            return new Error("requests.closed", "Başvuru kapatılmış.");
        }

        Status = RequestStatus.InProgress;
        return true;
    }

    public Result<bool> Close(bool completed, string? response, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return new Error("requests.closed", "Başvuru zaten kapatılmış.");
        }

        if (string.IsNullOrWhiteSpace(response))
        {
            return new Error("requests.response_required", "Başvurana verilen yanıtı yazın; kayıt olarak saklanır.");
        }

        Status = completed ? RequestStatus.Completed : RequestStatus.Rejected;
        Response = response.Trim();
        ClosedAt = now;
        return true;
    }

    public bool IsOverdue(DateOnly today) => IsOpen && today > DueOn;
}

/// <summary>Kişinin anonimleştirildiğine dair kanıt kaydı; kim, ne zaman, hangi başvuru üzerine.</summary>
public sealed class Anonymization : Entity
{
    private Anonymization()
    {
    }

    public Guid ContactId { get; private set; }

    public Guid? RequestId { get; private set; }

    public DateTimeOffset PerformedAt { get; private set; }

    public static Anonymization Create(Guid contactId, Guid? requestId, DateTimeOffset now) =>
        new() { ContactId = contactId, RequestId = requestId, PerformedAt = now };
}
