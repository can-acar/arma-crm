using Arma.Modules.Events.Infrastructure;
using Arma.Modules.Events.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Events.Application;

internal sealed class EventsPersonalData(EventsDbContext db, EventService events) : IPersonalDataSource
{
    public int Order => 260;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Etkinlikler", "Etkinlik kaydı, bilet türü, ödeme durumu, giriş saati", "Etkinlik katılımı", "Kayıt, kontenjan, giriş kontrolü ve katılım belgesi", "Sözleşmenin ifası", "Etkinlikten sonra 2 yıl"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken) =>
    [
        new("Etkinlik kayıtları", [.. (await events.ForContactAsync(contactId, cancellationToken)).Select(r => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
        {
            ["Etkinlik"] = r.EventTitle,
            ["Tarih"] = r.StartsAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            ["Bilet"] = r.TicketName,
            ["Durum"] = EventLabels.Status(r.Status),
            ["Ödeme"] = EventLabels.Payment(r.Payment),
            ["Giriş"] = r.CheckedInAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
        })]),
    ];

    public Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken) =>
        db.Registrations.Where(r => r.ContactId == contactId && r.Note != null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Note, (string?)null), cancellationToken);
}
