using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Arma.SharedKernel.Domain;

namespace Arma.Modules.Events.Domain;

public enum EventStatus
{
    Draft = 1,
    Published = 2,
    Cancelled = 3,
}

/// <summary>Seminer, gala, eğitim, kermes gibi. Yayımlanan etkinliğin /e/{adres} sayfasından kayıt alınır.</summary>
public sealed partial class Event : Entity
{
    private Event()
    {
    }

    public string Title { get; private set; } = "";

    public string Slug { get; private set; } = "";

    public string? Description { get; private set; }

    public string? Location { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Toplam kontenjan; boşsa sınırsız. Dolunca yeni kayıtlar yedek listeye alınır.</summary>
    public int? Capacity { get; private set; }

    /// <summary>Bu andan sonra web kaydı kapanır; boşsa etkinlik başlayana kadar açıktır.</summary>
    public DateTimeOffset? RegistrationClosesAt { get; private set; }

    public EventStatus Status { get; private set; } = EventStatus.Draft;

    /// <summary>Kayıt onayı e-postasına eklenen not (adres tarifi, yanınızda getirin...).</summary>
    public string? ConfirmationNote { get; private set; }

    public bool IsRegistrationOpen(DateTimeOffset now) =>
        Status == EventStatus.Published && now < (RegistrationClosesAt ?? StartsAt);

    public static Result<Event> Create(EventDetails details)
    {
        var item = new Event();
        var result = item.Update(details);
        return result.IsSuccess ? item : result.Error!;
    }

    public Result<bool> Update(EventDetails details)
    {
        var title = details.Title?.Trim() ?? "";
        if (title.Length is 0 or > 200)
        {
            return new Error("events.title", "Etkinlik adı 1-200 karakter olmalı.");
        }

        var slug = details.Slug?.Trim().ToLowerInvariant() ?? "";
        if (slug.Length is 0 or > 80 || !SlugPattern().IsMatch(slug))
        {
            return new Error("events.slug", "Adres yalnızca küçük harf, rakam ve tire içerebilir (örnek: bahar-semineri).");
        }

        if (details.EndsAt < details.StartsAt)
        {
            return new Error("events.dates", "Bitiş, başlangıçtan önce olamaz.");
        }

        if (details.Capacity is < 1 or > 1_000_000)
        {
            return new Error("events.capacity", "Kontenjan en az 1 olmalı; sınırsız için boş bırakın.");
        }

        Title = title;
        Slug = slug;
        Description = Clean(details.Description);
        Location = Clean(details.Location);
        StartsAt = details.StartsAt;
        EndsAt = details.EndsAt;
        Capacity = details.Capacity;
        RegistrationClosesAt = details.RegistrationClosesAt;
        ConfirmationNote = Clean(details.ConfirmationNote);
        return true;
    }

    public void Publish() => Status = EventStatus.Published;

    public void Unpublish() => Status = EventStatus.Draft;

    public void Cancel() => Status = EventStatus.Cancelled;

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

public sealed record EventDetails(
    string Title, string Slug, string? Description, string? Location, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    int? Capacity, DateTimeOffset? RegistrationClosesAt, string? ConfirmationNote);

/// <summary>Bilet türü: tam, öğrenci, davetli gibi. Etkinlikte hiç bilet türü yoksa kayıt ücretsizdir.</summary>
public sealed class TicketType : Entity
{
    private TicketType()
    {
    }

    public Guid EventId { get; private set; }

    public string Name { get; private set; } = "";

    public decimal Price { get; private set; }

    /// <summary>Bu türe ayrılan kontenjan; boşsa yalnızca etkinlik kontenjanı geçerli.</summary>
    public int? Capacity { get; private set; }

    /// <summary>Kapalı tür web sayfasında görünmez; personel yine de kayıt yapabilir (davetli gibi).</summary>
    public bool IsPublic { get; private set; } = true;

    public static Result<TicketType> Create(Guid eventId, string name, decimal price, int? capacity, bool isPublic)
    {
        var ticket = new TicketType { EventId = eventId };
        var result = ticket.Update(name, price, capacity, isPublic);
        return result.IsSuccess ? ticket : result.Error!;
    }

    public Result<bool> Update(string name, decimal price, int? capacity, bool isPublic)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
        {
            return new Error("tickets.name", "Bilet adı 1-100 karakter olmalı.");
        }

        if (price is < 0 or > 10_000_000)
        {
            return new Error("tickets.price", "Bilet fiyatı geçerli değil.");
        }

        if (capacity is < 1)
        {
            return new Error("tickets.capacity", "Kontenjan en az 1 olmalı; sınırsız için boş bırakın.");
        }

        Name = name;
        Price = decimal.Round(price, 2);
        Capacity = capacity;
        IsPublic = isPublic;
        return true;
    }
}

public enum RegistrationStatus
{
    Registered = 1,

    /// <summary>Kontenjan dolu; bir kayıt iptal edilince sırayla kesin kayda alınır.</summary>
    Waitlisted = 2,
    Cancelled = 3,
}

public enum PaymentState
{
    Free = 1,
    Unpaid = 2,
    Paid = 3,
}

/// <summary>Bir kişinin etkinliğe kaydı. Bilet kodu karekodla okutulur.</summary>
public sealed class Registration : Entity
{
    private Registration()
    {
    }

    public Guid EventId { get; private set; }

    public Guid ContactId { get; private set; }

    public Guid? TicketTypeId { get; private set; }

    public RegistrationStatus Status { get; private set; }

    public PaymentState Payment { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>Tahmin edilemeyen bilet kodu; bilet sayfası ve giriş okutması bununla yapılır.</summary>
    public string Code { get; private set; } = "";

    /// <summary>"web" veya "staff".</summary>
    public string Source { get; private set; } = "staff";

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? CheckedInAt { get; private set; }

    public string? Note { get; private set; }

    public bool ReminderSent { get; set; }

    public static Registration Create(Guid eventId, Guid contactId, TicketType? ticket, bool waitlisted, string source, string? note, DateTimeOffset now) => new()
    {
        EventId = eventId,
        ContactId = contactId,
        TicketTypeId = ticket?.Id,
        Status = waitlisted ? RegistrationStatus.Waitlisted : RegistrationStatus.Registered,
        Amount = ticket?.Price ?? 0,
        Payment = (ticket?.Price ?? 0) > 0 ? PaymentState.Unpaid : PaymentState.Free,
        Code = NewCode(),
        Source = source,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        RegisteredAt = now,
    };

    public Result<bool> CheckIn(DateTimeOffset now)
    {
        if (Status != RegistrationStatus.Registered)
        {
            return new Error("registrations.not_registered", Status == RegistrationStatus.Waitlisted ? "Kayıt yedek listede; giriş yapılamaz." : "Kayıt iptal edilmiş.");
        }

        if (CheckedInAt is not null)
        {
            return new Error("registrations.already_checked_in", $"Bu bilet {CheckedInAt.Value.ToLocalTime():HH:mm} saatinde okutulmuş.");
        }

        CheckedInAt = now;
        return true;
    }

    public void UndoCheckIn() => CheckedInAt = null;

    public void Cancel() => Status = RegistrationStatus.Cancelled;

    public void Confirm() => Status = RegistrationStatus.Registered;

    public void MarkPaid(bool paid) => Payment = Amount <= 0 ? PaymentState.Free : paid ? PaymentState.Paid : PaymentState.Unpaid;

    public void MoveTo(Guid contactId) => ContactId = contactId;

    /// <summary>Karışabilecek harfler (0/O, 1/I/L) olmadan 10 karakter.</summary>
    private static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        return string.Create(10, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }
        });
    }
}
