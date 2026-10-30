using System.Globalization;
using System.Text;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Comms.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Events.Domain;
using Arma.Modules.Events.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Events.Application;

public sealed record EventSummary(
    Guid Id, string Title, string Slug, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Location, EventStatus Status,
    int? Capacity, int Registered, int Waitlisted, int CheckedIn);

public sealed record TicketView(Guid Id, string Name, decimal Price, int? Capacity, bool IsPublic, int Registered)
{
    public bool SoldOut => Capacity is { } c && Registered >= c;
}

public sealed record EventView(
    Guid Id, string Title, string Slug, string? Description, string? Location, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    int? Capacity, DateTimeOffset? RegistrationClosesAt, EventStatus Status, string? ConfirmationNote,
    IReadOnlyList<TicketView> Tickets, int Registered, int Waitlisted, int CheckedIn, bool RegistrationOpen)
{
    public bool Full => Capacity is { } c && Registered >= c;

    public EventDetails Details => new(Title, Slug, Description, Location, StartsAt, EndsAt, Capacity, RegistrationClosesAt, ConfirmationNote);
}

public sealed record ParticipantView(
    Guid Id, Guid EventId, Guid ContactId, string Name, string? Email, string? Phone, Guid? TicketTypeId, string? TicketName,
    RegistrationStatus Status, PaymentState Payment, decimal Amount, string Code, string Source, DateTimeOffset RegisteredAt,
    DateTimeOffset? CheckedInAt, string? Note);

public sealed record ContactRegistrationView(Guid Id, Guid EventId, string EventTitle, DateTimeOffset StartsAt, string? TicketName, RegistrationStatus Status, PaymentState Payment, DateTimeOffset? CheckedInAt);

public sealed record TicketPageView(string Code, string Name, string EventTitle, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Location, string? TicketName,
    RegistrationStatus Status, PaymentState Payment, decimal Amount, DateTimeOffset? CheckedInAt, EventStatus EventStatus);

public sealed record CheckInResult(Guid RegistrationId, string Name, string? TicketName, PaymentState Payment, decimal Amount);

public sealed record PublicRegistration(string FirstName, string LastName, string Email, string? Phone, Guid? TicketTypeId);

public static class EventErrors
{
    public static readonly Error NotFound = new("events.not_found", "Etkinlik bulunamadı.");
    public static readonly Error RegistrationNotFound = new("registrations.not_found", "Kayıt bulunamadı.");
    public static readonly Error TicketNotFound = new("tickets.not_found", "Bilet türü bulunamadı.");
    public static readonly Error TicketRequired = new("tickets.required", "Bilet türü seçin.");
    public static readonly Error TicketInUse = new("tickets.in_use", "Bu bilet türüyle kayıt var; silinemez.");
    public static readonly Error DuplicateSlug = new("events.duplicate_slug", "Bu adres başka bir etkinlikte kullanılıyor.");
    public static readonly Error AlreadyRegistered = new("registrations.duplicate", "Kişinin bu etkinlikte kaydı zaten var.");
    public static readonly Error Closed = new("events.closed", "Bu etkinliğe kayıt kapandı.");
    public static readonly Error Cancelled = new("events.cancelled", "Etkinlik iptal edilmiş.");
    public static readonly Error UnknownCode = new("registrations.unknown_code", "Bu bilet kodu bu etkinliğe ait değil.");
}

public sealed class EventService(
    EventsDbContext db, IContactDirectory contacts, IContactRegistry registry, IDbContextOutbox outbox, TimeProvider clock, IConfiguration configuration)
{
    /// <summary>E-postadaki bilet bağlantısı için kurumun dışarıdan erişilen adresi (ARMA_PUBLIC_URL).</summary>
    private string? PublicUrl => configuration["Comms:PublicUrl"]?.TrimEnd('/') is { Length: > 0 } url ? url : null;

    internal const string SourceModule = "Events";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<IReadOnlyList<EventSummary>> ListAsync(bool past = false, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var events = db.Events.AsNoTracking().Where(e => past ? e.EndsAt < now : e.EndsAt >= now);
        events = past ? events.OrderByDescending(e => e.StartsAt) : events.OrderBy(e => e.StartsAt);
        return await events.Take(200)
            .Select(e => new EventSummary(e.Id, e.Title, e.Slug, e.StartsAt, e.EndsAt, e.Location, e.Status, e.Capacity,
                db.Registrations.Count(r => r.EventId == e.Id && r.Status == RegistrationStatus.Registered),
                db.Registrations.Count(r => r.EventId == e.Id && r.Status == RegistrationStatus.Waitlisted),
                db.Registrations.Count(r => r.EventId == e.Id && r.CheckedInAt != null)))
            .ToListAsync(cancellationToken);
    }

    public async Task<EventView?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await ViewAsync(db.Events.AsNoTracking().Where(e => e.Id == id), false, cancellationToken);

    /// <summary>Kamuya açık sayfa için: yayımlanmış etkinlik ve yalnızca açık bilet türleri.</summary>
    public async Task<EventView?> PublishedAsync(string slug, CancellationToken cancellationToken = default) =>
        await ViewAsync(db.Events.AsNoTracking().Where(e => e.Slug == slug && e.Status != EventStatus.Draft), true, cancellationToken);

    public async Task<Result<Guid>> SaveAsync(Guid? id, EventDetails details, CancellationToken cancellationToken = default)
    {
        var slug = details.Slug?.Trim().ToLowerInvariant();
        if (await db.Events.AnyAsync(e => e.Slug == slug && e.Id != id, cancellationToken))
        {
            return EventErrors.DuplicateSlug;
        }

        Event item;
        if (id is null)
        {
            var created = Event.Create(details);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            item = created.Value!;
            db.Events.Add(item);
        }
        else
        {
            var found = await db.Events.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (found is null)
            {
                return EventErrors.NotFound;
            }

            var updated = found.Update(details);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }

            item = found;
        }

        await db.SaveChangesAsync(cancellationToken);
        return item.Id;
    }

    public Task<Result<Guid>> PublishAsync(Guid id, bool publish, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, e =>
        {
            if (e.Status == EventStatus.Cancelled)
            {
                return EventErrors.Cancelled;
            }

            if (publish)
            {
                e.Publish();
            }
            else
            {
                e.Unpublish();
            }

            return true;
        }, cancellationToken);

    /// <summary>Etkinliği iptal eder ve kayıtlı/yedek herkese bilgi e-postası gönderir.</summary>
    public async Task<Result<Guid>> CancelEventAsync(Guid id, string? message, CancellationToken cancellationToken = default)
    {
        var item = await db.Events.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (item is null)
        {
            return EventErrors.NotFound;
        }

        if (item.Status == EventStatus.Cancelled)
        {
            return EventErrors.Cancelled;
        }

        item.Cancel();
        var people = await db.Registrations.Where(r => r.EventId == id && r.Status != RegistrationStatus.Cancelled).Select(r => r.ContactId).Distinct().ToListAsync(cancellationToken);
        outbox.Enroll(db);
        foreach (var contactId in people)
        {
            await outbox.PublishAsync(new SendTransactionalEmail(contactId, $"İptal: {item.Title}",
                $"{When(item)} tarihindeki \"{item.Title}\" etkinliği iptal edildi.{(string.IsNullOrWhiteSpace(message) ? "" : $"\n\n{message.Trim()}")}"));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> SaveTicketAsync(Guid eventId, Guid? ticketId, string name, decimal price, int? capacity, bool isPublic, CancellationToken cancellationToken = default)
    {
        if (!await db.Events.AnyAsync(e => e.Id == eventId, cancellationToken))
        {
            return EventErrors.NotFound;
        }

        if (ticketId is null)
        {
            var created = TicketType.Create(eventId, name, price, capacity, isPublic);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            db.TicketTypes.Add(created.Value!);
            await db.SaveChangesAsync(cancellationToken);
            return created.Value!.Id;
        }

        var ticket = await db.TicketTypes.FirstOrDefaultAsync(t => t.Id == ticketId && t.EventId == eventId, cancellationToken);
        if (ticket is null)
        {
            return EventErrors.TicketNotFound;
        }

        var updated = ticket.Update(name, price, capacity, isPublic);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ticket.Id;
    }

    public async Task<Result<Guid>> DeleteTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await db.TicketTypes.FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken);
        if (ticket is null)
        {
            return EventErrors.TicketNotFound;
        }

        if (await db.Registrations.AnyAsync(r => r.TicketTypeId == ticketId, cancellationToken))
        {
            return EventErrors.TicketInUse;
        }

        db.TicketTypes.Remove(ticket);
        await db.SaveChangesAsync(cancellationToken);
        return ticketId;
    }

    /// <summary>
    /// Kişiyi etkinliğe kaydeder. Etkinlik veya bilet kontenjanı doluysa yedek listeye alır. Aynı anda gelen
    /// kayıtların kontenjanı aşmaması için etkinlik satırı kilitlenir. Kişiye bilet bağlantılı onay e-postası gider.
    /// </summary>
    public async Task<Result<Guid>> RegisterAsync(Guid eventId, Guid contactId, Guid? ticketTypeId, string source, string? note, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM events.events WHERE id = {eventId} FOR UPDATE", cancellationToken);
        var item = await db.Events.FirstOrDefaultAsync(e => e.Id == eventId, cancellationToken);
        if (item is null)
        {
            return EventErrors.NotFound;
        }

        if (item.Status == EventStatus.Cancelled)
        {
            return EventErrors.Cancelled;
        }

        var tickets = await db.TicketTypes.Where(t => t.EventId == eventId).ToListAsync(cancellationToken);
        TicketType? ticket = null;
        if (tickets.Count > 0)
        {
            ticket = tickets.FirstOrDefault(t => t.Id == ticketTypeId);
            if (ticket is null)
            {
                return ticketTypeId is null ? EventErrors.TicketRequired : EventErrors.TicketNotFound;
            }
        }

        if (await db.Registrations.AnyAsync(r => r.EventId == eventId && r.ContactId == contactId && r.Status != RegistrationStatus.Cancelled, cancellationToken))
        {
            return EventErrors.AlreadyRegistered;
        }

        var waitlisted = !await HasRoomAsync(item, ticket, cancellationToken);
        var registration = Registration.Create(eventId, contactId, ticket, waitlisted, source, note, clock.GetUtcNow());
        db.Registrations.Add(registration);
        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(item, registration, waitlisted ? $"Etkinlik yedek listesi: {item.Title}" : $"Etkinliğe kayıt: {item.Title}"));
        await outbox.PublishAsync(Confirmation(item, registration, ticket));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        if (db.Database.CurrentTransaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return registration.Id;
    }

    /// <summary>Kamuya açık sayfadan kayıt: kişi e-postayla bulunur veya açılır, kapalı bilet türü seçilemez.</summary>
    public async Task<Result<string>> RegisterPublicAsync(string slug, PublicRegistration input, CancellationToken cancellationToken = default)
    {
        var item = await db.Events.AsNoTracking().FirstOrDefaultAsync(e => e.Slug == slug && e.Status == EventStatus.Published, cancellationToken);
        if (item is null)
        {
            return EventErrors.NotFound;
        }

        if (!item.IsRegistrationOpen(clock.GetUtcNow()))
        {
            return EventErrors.Closed;
        }

        if (input.TicketTypeId is { } ticketId && !await db.TicketTypes.AnyAsync(t => t.Id == ticketId && t.EventId == item.Id && t.IsPublic, cancellationToken))
        {
            return EventErrors.TicketNotFound;
        }

        if (string.IsNullOrWhiteSpace(input.Email))
        {
            return new Error("registrations.email", "E-posta adresi gerekli; biletiniz bu adrese gönderilir.");
        }

        var person = await registry.FindOrCreatePersonAsync(input.FirstName, input.LastName, input.Email, input.Phone, cancellationToken);
        if (person.Error is not null)
        {
            return new Error("registrations.contact", person.Error);
        }

        var registered = await RegisterAsync(item.Id, person.Id!.Value, input.TicketTypeId, "web", null, cancellationToken);
        if (registered.Error == EventErrors.AlreadyRegistered)
        {
            return new Error("registrations.duplicate", "Bu e-posta adresiyle zaten kaydınız var. Biletiniz e-postanızda.");
        }

        return registered.IsSuccess
            ? await db.Registrations.Where(r => r.Id == registered.Value).Select(r => r.Code).FirstAsync(cancellationToken)
            : registered.Error!;
    }

    /// <summary>Kaydı iptal eder; kesin kayıt boşalırsa yedek listedeki ilk uygun kişi kesin kayda alınır ve bilgilendirilir.</summary>
    public async Task<Result<Guid>> CancelRegistrationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var registration = await db.Registrations.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (registration is null)
        {
            return EventErrors.RegistrationNotFound;
        }

        if (registration.Status == RegistrationStatus.Cancelled)
        {
            return id;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM events.events WHERE id = {registration.EventId} FOR UPDATE", cancellationToken);
        var item = await db.Events.FirstAsync(e => e.Id == registration.EventId, cancellationToken);
        var freed = registration.Status == RegistrationStatus.Registered;
        registration.Cancel();
        outbox.Enroll(db);
        await outbox.PublishAsync(new RemoveTimelineEntry(SourceModule, registration.Id.ToString()));
        if (freed && item.Status != EventStatus.Cancelled)
        {
            await db.SaveChangesAsync(cancellationToken);
            await PromoteWaitlistAsync(item, cancellationToken);
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        if (db.Database.CurrentTransaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return id;
    }

    public async Task<Result<Guid>> MarkPaidAsync(Guid id, bool paid, CancellationToken cancellationToken = default)
    {
        var registration = await db.Registrations.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (registration is null)
        {
            return EventErrors.RegistrationNotFound;
        }

        registration.MarkPaid(paid);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>Girişte bilet kodunu okutur. Kod büyük/küçük harf ve boşluk farkı gözetmez.</summary>
    public async Task<Result<CheckInResult>> CheckInAsync(Guid eventId, string code, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(code);
        var registration = await db.Registrations.FirstOrDefaultAsync(r => r.Code == normalized && r.EventId == eventId, cancellationToken);
        if (registration is null)
        {
            return EventErrors.UnknownCode;
        }

        var checkedIn = registration.CheckIn(clock.GetUtcNow());
        if (!checkedIn.IsSuccess)
        {
            return checkedIn.Error!;
        }

        var item = await db.Events.AsNoTracking().FirstAsync(e => e.Id == eventId, cancellationToken);
        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(item, registration, $"Etkinliğe katıldı: {item.Title}"));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        var contact = await contacts.FindAsync(registration.ContactId, cancellationToken);
        var ticketName = registration.TicketTypeId is { } t ? await db.TicketTypes.Where(x => x.Id == t).Select(x => x.Name).FirstOrDefaultAsync(cancellationToken) : null;
        return new CheckInResult(registration.Id, contact?.DisplayName ?? "(silinmiş kişi)", ticketName, registration.Payment, registration.Amount);
    }

    public async Task<Result<Guid>> UndoCheckInAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var registration = await db.Registrations.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (registration is null)
        {
            return EventErrors.RegistrationNotFound;
        }

        registration.UndoCheckIn();
        var item = await db.Events.AsNoTracking().FirstAsync(e => e.Id == registration.EventId, cancellationToken);
        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(item, registration, $"Etkinliğe kayıt: {item.Title}"));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<ParticipantView>> ParticipantsAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Registrations.AsNoTracking().Where(r => r.EventId == eventId)
            .OrderBy(r => r.Status).ThenBy(r => r.RegisteredAt)
            .Select(r => new { Registration = r, Ticket = db.TicketTypes.Where(t => t.Id == r.TicketTypeId).Select(t => t.Name).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        var people = await contacts.FindManyAsync([.. rows.Select(r => r.Registration.ContactId).Distinct()], cancellationToken);
        return [.. rows.Select(x =>
        {
            var r = x.Registration;
            var person = people.GetValueOrDefault(r.ContactId);
            return new ParticipantView(r.Id, r.EventId, r.ContactId, person?.DisplayName ?? "(silinmiş kişi)", person?.Email, person?.Phone, r.TicketTypeId, x.Ticket,
                r.Status, r.Payment, r.Amount, r.Code, r.Source, r.RegisteredAt, r.CheckedInAt, r.Note);
        })];
    }

    public async Task<byte[]> ParticipantsCsvAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder("Ad;E-posta;Telefon;Bilet;Durum;Ödeme;Tutar;Kayıt;Giriş;Kod\n");
        foreach (var p in await ParticipantsAsync(eventId, cancellationToken))
        {
            string[] cells = [p.Name, p.Email ?? "", p.Phone ?? "", p.TicketName ?? "", Web.EventLabels.Status(p.Status), Web.EventLabels.Payment(p.Payment),
                p.Amount.ToString("N2", Turkish), p.RegisteredAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), p.CheckedInAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "", p.Code];
            output.AppendLine(string.Join(';', cells.Select(c => c.Contains(';') || c.Contains('"') ? $"\"{c.Replace("\"", "\"\"")}\"" : c)));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(output.ToString())];
    }

    public async Task<IReadOnlyList<ContactRegistrationView>> ForContactAsync(Guid contactId, CancellationToken cancellationToken = default) =>
        await db.Registrations.AsNoTracking().Where(r => r.ContactId == contactId)
            .Join(db.Events, r => r.EventId, e => e.Id, (r, e) => new { r, e })
            .OrderByDescending(x => x.e.StartsAt)
            .Select(x => new ContactRegistrationView(x.r.Id, x.e.Id, x.e.Title, x.e.StartsAt,
                db.TicketTypes.Where(t => t.Id == x.r.TicketTypeId).Select(t => t.Name).FirstOrDefault(), x.r.Status, x.r.Payment, x.r.CheckedInAt))
            .ToListAsync(cancellationToken);

    /// <summary>Bilet sayfası; kod bilinmeden erişilemez.</summary>
    public async Task<TicketPageView?> TicketAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(code);
        var row = await db.Registrations.AsNoTracking().Where(r => r.Code == normalized)
            .Join(db.Events, r => r.EventId, e => e.Id, (r, e) => new { r, e })
            .Select(x => new { x.r, x.e, Ticket = db.TicketTypes.Where(t => t.Id == x.r.TicketTypeId).Select(t => t.Name).FirstOrDefault() })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var person = await contacts.FindAsync(row.r.ContactId, cancellationToken);
        return new TicketPageView(row.r.Code, person?.DisplayName ?? "", row.e.Title, row.e.StartsAt, row.e.EndsAt, row.e.Location, row.Ticket,
            row.r.Status, row.r.Payment, row.r.Amount, row.r.CheckedInAt, row.e.Status);
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        foreach (var registration in await db.Registrations.Where(r => r.ContactId == from).ToListAsync(cancellationToken))
        {
            registration.MoveTo(to);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal static string When(Event item) =>
        item.StartsAt.ToLocalTime().ToString("d MMMM yyyy dddd HH:mm", Turkish);

    private static string Normalize(string code) => new([.. (code ?? "").Trim().ToUpperInvariant().Where(char.IsLetterOrDigit)]);

    private async Task<Result<Guid>> ChangeAsync(Guid id, Func<Event, Result<bool>> change, CancellationToken cancellationToken)
    {
        var item = await db.Events.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (item is null)
        {
            return EventErrors.NotFound;
        }

        var result = change(item);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    private async Task<bool> HasRoomAsync(Event item, TicketType? ticket, CancellationToken cancellationToken)
    {
        var registered = db.Registrations.Where(r => r.EventId == item.Id && r.Status == RegistrationStatus.Registered);
        if (item.Capacity is { } capacity && await registered.CountAsync(cancellationToken) >= capacity)
        {
            return false;
        }

        return ticket?.Capacity is not { } ticketCapacity || await registered.CountAsync(r => r.TicketTypeId == ticket.Id, cancellationToken) < ticketCapacity;
    }

    private async Task PromoteWaitlistAsync(Event item, CancellationToken cancellationToken)
    {
        var waiting = await db.Registrations.Where(r => r.EventId == item.Id && r.Status == RegistrationStatus.Waitlisted)
            .OrderBy(r => r.RegisteredAt).ToListAsync(cancellationToken);
        var tickets = await db.TicketTypes.Where(t => t.EventId == item.Id).ToDictionaryAsync(t => t.Id, cancellationToken);
        foreach (var candidate in waiting)
        {
            var ticket = candidate.TicketTypeId is { } t ? tickets.GetValueOrDefault(t) : null;
            if (!await HasRoomAsync(item, ticket, cancellationToken))
            {
                continue;
            }

            candidate.Confirm();
            await outbox.PublishAsync(Timeline(item, candidate, $"Etkinliğe kayıt: {item.Title}"));
            await outbox.PublishAsync(new SendTransactionalEmail(candidate.ContactId, $"Yeriniz açıldı: {item.Title}",
                $"Yedek listede olduğunuz \"{item.Title}\" etkinliğinde yer açıldı ve kaydınız kesinleşti.\n\nTarih: {When(item)}\n\nBilet kodunuz: {candidate.Code}"));
            break;
        }
    }

    private async Task<EventView?> ViewAsync(IQueryable<Event> query, bool publicOnly, CancellationToken cancellationToken)
    {
        var item = await query.FirstOrDefaultAsync(cancellationToken);
        if (item is null)
        {
            return null;
        }

        var counts = await db.Registrations.Where(r => r.EventId == item.Id).GroupBy(r => new { r.Status, r.TicketTypeId, CheckedIn = r.CheckedInAt != null })
            .Select(g => new { g.Key.Status, g.Key.TicketTypeId, g.Key.CheckedIn, Count = g.Count() }).ToListAsync(cancellationToken);
        var tickets = await db.TicketTypes.AsNoTracking().Where(t => t.EventId == item.Id && (!publicOnly || t.IsPublic)).OrderBy(t => t.Price).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        return new EventView(item.Id, item.Title, item.Slug, item.Description, item.Location, item.StartsAt, item.EndsAt, item.Capacity, item.RegistrationClosesAt,
            item.Status, item.ConfirmationNote,
            [.. tickets.Select(t => new TicketView(t.Id, t.Name, t.Price, t.Capacity, t.IsPublic,
                counts.Where(c => c.TicketTypeId == t.Id && c.Status == RegistrationStatus.Registered).Sum(c => c.Count)))],
            counts.Where(c => c.Status == RegistrationStatus.Registered).Sum(c => c.Count),
            counts.Where(c => c.Status == RegistrationStatus.Waitlisted).Sum(c => c.Count),
            counts.Where(c => c.CheckedIn).Sum(c => c.Count),
            item.IsRegistrationOpen(clock.GetUtcNow()));
    }

    private RecordTimelineEntry Timeline(Event item, Registration registration, string subject) =>
        new([registration.ContactId], ActivityKind.System, subject, When(item), registration.RegisteredAt, SourceModule, registration.Id.ToString());

    private SendTransactionalEmail Confirmation(Event item, Registration registration, TicketType? ticket)
    {
        var body = new StringBuilder();
        body.AppendLine(registration.Status == RegistrationStatus.Waitlisted
            ? $"\"{item.Title}\" etkinliğinin kontenjanı dolu olduğu için yedek listeye alındınız. Yer açılırsa size haber vereceğiz."
            : $"\"{item.Title}\" etkinliğine kaydınız alındı.");
        body.AppendLine().AppendLine($"Tarih: {When(item)}");
        if (item.Location is not null)
        {
            body.AppendLine($"Yer: {item.Location}");
        }

        if (ticket is not null)
        {
            body.AppendLine($"Bilet: {ticket.Name}{(ticket.Price > 0 ? $" ({ticket.Price.ToString("N2", Turkish)} TL, ödeme bekleniyor)" : "")}");
        }

        body.AppendLine().AppendLine($"Bilet kodunuz: {registration.Code}");
        if (PublicUrl is { } url)
        {
            body.AppendLine($"Biletiniz: {url}/e/bilet/{registration.Code}");
        }
        if (item.ConfirmationNote is not null)
        {
            body.AppendLine().AppendLine(item.ConfirmationNote);
        }

        return new SendTransactionalEmail(registration.ContactId,
            registration.Status == RegistrationStatus.Waitlisted ? $"Yedek listedesiniz: {item.Title}" : $"Kaydınız alındı: {item.Title}", body.ToString());
    }
}
