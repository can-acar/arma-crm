using System.Globalization;
using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Application;

internal sealed class ContactsPersonalData(ContactsDbContext db, TimeProvider clock) : IPersonalDataSource
{
    private const string Module = "Kişiler";

    public int Order => 1000;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new(Module, "Ad, soyad", "Kimlik", "Kişiyle iletişim, bağış ve üyelik kayıtları", "Sözleşmenin ifası, meşru menfaat", "Son işlemden 10 yıl"),
        new(Module, "T.C. kimlik numarası", "Kimlik", "Bağış makbuzu ve vergi indirimi", "Kanuni yükümlülük (VUK)", "Son işlemden 10 yıl"),
        new(Module, "Doğum tarihi", "Kimlik", "Üyelik ve yaş koşulları", "Açık rıza", "Kayıt silinene kadar"),
        new(Module, "E-posta, telefon", "İletişim", "Bilgilendirme ve ticari ileti", "Sözleşmenin ifası; ticari iletide açık rıza", "Kayıt silinene kadar"),
        new(Module, "Adres", "İletişim", "Makbuz ve yazışma", "Sözleşmenin ifası", "Son işlemden 10 yıl"),
        new(Module, "Etiket, grup, ilişki", "Diğer", "Kişileri sınıflandırma, hane ve kurum bağları", "Meşru menfaat", "Kayıt silinene kadar"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var contact = await db.Contacts.AsNoTracking().Include(c => c.Tags).Include(c => c.Groups)
            .FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null)
        {
            return [];
        }

        var relationships = await db.Relationships.AsNoTracking()
            .Where(r => r.FromContactId == contactId || r.ToContactId == contactId)
            .Join(db.Contacts, r => r.FromContactId == contactId ? r.ToContactId : r.FromContactId, c => c.Id, (r, c) => new { r.Type, r.Description, Other = c.DisplayName })
            .ToListAsync(cancellationToken);

        return
        [
            new("Kişi bilgileri", [new Dictionary<string, string?>
            {
                ["Ad"] = contact.FirstName ?? contact.OrganizationName,
                ["Soyad"] = contact.LastName,
                ["Görünen ad"] = contact.DisplayName,
                ["E-posta"] = contact.Email,
                ["Telefon"] = contact.Phone,
                ["Kimlik numarası"] = contact.NationalId,
                ["Doğum tarihi"] = contact.BirthDate?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                ["Web"] = contact.Website,
                ["Kayıt tarihi"] = contact.CreatedAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
            }]),
            new("Adresler", [.. contact.Addresses.Select(a => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Tür"] = Address.KindLabel(a.Kind),
                ["Adres"] = a.ToString(),
            })]),
            new("Etiket ve gruplar", [.. contact.Tags.Select(t => Row("Etiket", t.Name)).Concat(contact.Groups.Select(g => Row("Grup", g.Name)))]),
            new("İlişkiler", [.. relationships.Select(r => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Kayıt"] = r.Other,
                ["İlişki"] = r.Type.ToString(),
                ["Açıklama"] = r.Description,
            })]),
        ];
    }

    public async Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken)
    {
        // Değişiklik denetim izinden geçmesin diye (eski değerler yeniden yazılmasın) doğrudan SQL ile yapılır.
        var now = clock.GetUtcNow();
        var label = "Anonim kişi " + contactId.ToString("N")[..6];
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE contacts.contacts SET
                first_name = CASE WHEN type = 1 THEN 'Anonim' END, last_name = NULL,
                organization_name = CASE WHEN type <> 1 THEN {label} END,
                display_name = {label}, email = NULL, phone = NULL, national_id = NULL, birth_date = NULL,
                website = NULL, addresses = '[]', search_text = 'anonim', anonymized_at = {now}, updated_at = {now}
            WHERE id = {contactId}
            """, cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM contacts.contact_tags WHERE contacts_id = {contactId}", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM contacts.group_members WHERE members_id = {contactId}", cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM contacts.relationships WHERE from_contact_id = {contactId} OR to_contact_id = {contactId}", cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static IReadOnlyDictionary<string, string?> Row(string kind, string name) =>
        new Dictionary<string, string?> { ["Tür"] = kind, ["Ad"] = name };
}
