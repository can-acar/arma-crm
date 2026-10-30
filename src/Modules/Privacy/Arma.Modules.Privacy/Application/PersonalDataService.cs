using System.Text.Json;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Privacy.Domain;
using Arma.Modules.Privacy.Infrastructure;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Privacy.Application;

/// <summary>Kişisel veri envanteri, kişinin tüm verisini dışa aktarma ve anonimleştirme.</summary>
public sealed class PersonalDataService(
    PrivacyDbContext db,
    IEnumerable<IPersonalDataSource> sources,
    IContactDirectory contacts,
    IAuditLog audit,
    TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public IReadOnlyList<PersonalDataItem> Inventory() =>
        sources.OrderByDescending(s => s.Order).SelectMany(s => s.Inventory).ToList();

    /// <summary>Envanteri VERBİS girişine hazırlık için CSV olarak verir.</summary>
    public byte[] InventoryCsv()
    {
        static string Cell(string value) => value.Contains(';') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
        var lines = new List<string> { "Modül;Veri;Kategori;Amaç;Hukuki sebep;Saklama süresi" };
        lines.AddRange(Inventory().Select(i => string.Join(';', new[] { i.Module, i.Data, i.Category, i.Purpose, i.LegalBasis, i.Retention }.Select(Cell))));
        return [.. System.Text.Encoding.UTF8.GetPreamble(), .. System.Text.Encoding.UTF8.GetBytes(string.Join("\r\n", lines))];
    }

    public async Task<Result<byte[]>> ExportAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var contact = await contacts.FindAsync(contactId, cancellationToken);
        if (contact is null)
        {
            return PrivacyErrors.ContactNotFound;
        }

        var sections = new List<PersonalDataSection>();
        foreach (var source in sources.OrderByDescending(s => s.Order))
        {
            sections.AddRange((await source.ExportAsync(contact.Id, cancellationToken)).Where(s => s.Rows.Count > 0));
        }

        await audit.WriteAsync(AuditAction.Exported, "Contact", contact.Id.ToString(), "KVKK: kişinin tüm verisi dışa aktarıldı", cancellationToken);
        var document = new
        {
            Kisi = contact.DisplayName,
            Olusturulma = clock.GetLocalNow().ToString("dd.MM.yyyy HH:mm"),
            Bolumler = sections.Select(s => new { Baslik = s.Title, Kayitlar = s.Rows }),
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, Json);
    }

    public async Task<DateTimeOffset?> AnonymizedAtAsync(Guid contactId, CancellationToken cancellationToken = default) =>
        await db.Anonymizations.Where(a => a.ContactId == contactId).Select(a => (DateTimeOffset?)a.PerformedAt).FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Kişiyi tüm modüllerde anonimleştirir. Geri alınamaz. Kurum kayıtları KVKK kapsamında olmadığı için anonimleştirilmez.
    /// </summary>
    public async Task<Result<Guid>> AnonymizeAsync(Guid contactId, Guid? requestId, CancellationToken cancellationToken = default)
    {
        var contact = await contacts.FindAsync(contactId, cancellationToken);
        if (contact is null || contact.Id != contactId)
        {
            return PrivacyErrors.ContactNotFound;
        }

        if (contact.Type == ContactType.Organization)
        {
            return new Error("privacy.organization", "Kurum kayıtları kişisel veri olmadığı için anonimleştirilmez.");
        }

        if (await AnonymizedAtAsync(contactId, cancellationToken) is not null)
        {
            return new Error("privacy.already_anonymized", "Kişi zaten anonimleştirilmiş.");
        }

        foreach (var source in sources.OrderBy(s => s.Order))
        {
            await source.AnonymizeAsync(contactId, cancellationToken);
        }

        var record = Anonymization.Create(contactId, requestId, clock.GetUtcNow());
        db.Anonymizations.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(AuditAction.Anonymized, "Contact", contactId.ToString(), "KVKK: kişi anonimleştirildi", cancellationToken);
        return record.Id;
    }
}

/// <summary>KVKK modülünün kendi tuttuğu kişisel veri: rıza kayıtları ve başvurular. Kanıt olarak saklanır, anonimleştirmede silinmez.</summary>
internal sealed class PrivacyPersonalData(PrivacyDbContext db, ConsentRecordService consents) : IPersonalDataSource
{
    public int Order => 50;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("KVKK", "Açık rıza kayıtları (metin sürümü, tarih, IP)", "İşlem güvenliği", "Rızanın ispatı", "Kanuni yükümlülük (KVKK m.5, m.12)", "Rıza geri alındıktan sonra 10 yıl"),
        new("KVKK", "İlgili kişi başvuruları ve yanıtları", "Hukuki işlem", "Başvurunun yanıtlanması ve ispatı", "Kanuni yükümlülük (KVKK m.13)", "Başvuru kapandıktan sonra 10 yıl"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var history = await consents.HistoryAsync(contactId, cancellationToken);
        var requests = await db.Requests.AsNoTracking().Where(r => r.ContactId == contactId).OrderBy(r => r.ReceivedOn).ToListAsync(cancellationToken);
        return
        [
            new("Açık rıza kayıtları", [.. history.Select(h => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Metin"] = $"{h.Title} (sürüm {h.VersionNumber})",
                ["Durum"] = h.Granted ? "Rıza verildi" : "Rıza geri alındı",
                ["Tarih"] = h.RecordedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                ["Kaynak"] = h.Source,
            })]),
            new("Başvurular", [.. requests.Select(r => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Tarih"] = r.ReceivedOn.ToString("dd.MM.yyyy"),
                ["Konu"] = Web.PrivacyLabels.Type(r.Type),
                ["Başvuru"] = r.Description,
                ["Yanıt"] = r.Response,
            })]),
        ];
    }

    public Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class PrivacyEventHandler
{
    public static Task Handle(ContactsMerged message, DataRequestService requests, CancellationToken cancellationToken) =>
        requests.MoveContactAsync(message.DuplicateId, message.PrimaryId, cancellationToken);
}
