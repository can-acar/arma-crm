using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Membership.Domain;
using Arma.Modules.Membership.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Membership.Application;

/// <summary>
/// CiviCRM "Üyelikleri dışa aktar" (Export Memberships) dosyasını aktarır. Üyelik türleri adıyla eşleşir, yoksa 12 aylık
/// ücretsiz tür olarak açılır; tarihler olduğu gibi alınır, Arma kendi üye numarasını verir.
/// </summary>
public sealed class CivicrmMembershipImporter(MembershipDbContext db, MembershipService memberships, IContactRegistry registry) : IDataImporter
{
    private const string SourceKey = "civicrm";

    private const int BatchSize = 200;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public string Source => SourceKey;

    public string Label => "Üyelikler";

    public string Hint => "Üyelikleri Ara → tümünü seç → Üyelikleri dışa aktar → \"Birincil alanlar\". Bitiş tarihi boş üyelik ömür boyu sayılır.";

    public int Order => 30;

    public string Permission => MembershipPermissions.Write;

    public IReadOnlyList<string> RequiredColumns => ["Contact ID", "Membership Type", "Membership Start Date|Start Date|Member Since|Join Date"];

    public async Task<ImportOutcome> ImportAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        var issues = new List<ImportIssue>();
        var today = memberships.Today;
        var contactIds = await registry.FindByExternalKeysAsync(
            [.. rows.Select(r => r.Get("Contact ID")).OfType<string>().Select(id => ExternalKeys.For(SourceKey, id)).Distinct()], cancellationToken);
        var keys = rows.Select(r => Key(r)).ToList();
        var known = (await db.Members.Where(m => m.ExternalKey != null && keys.Contains(m.ExternalKey)).Select(m => m.ExternalKey!).ToListAsync(cancellationToken)).ToHashSet();
        var types = await TypesAsync(rows, cancellationToken);
        var batch = new List<(Member, MembershipType)>();
        int created = 0, skipped = 0;

        foreach (var (row, key) in rows.Zip(keys))
        {
            var name = row.Get("Display Name", "Sort Name") ?? $"Contact ID {row.Get("Contact ID")}";
            if (key is null)
            {
                issues.Add(new ImportIssue(row.Line, name, "Contact ID veya üyelik türü boş."));
                continue;
            }

            if (!known.Add(key))
            {
                skipped++;
                continue;
            }

            if (!contactIds.TryGetValue(ExternalKeys.For(SourceKey, row.Get("Contact ID")!), out var contactId))
            {
                issues.Add(new ImportIssue(row.Line, name, "Kişi bulunamadı; önce kişiler dosyasını aktarın."));
                continue;
            }

            if (row.Get("Membership Type") is not { } typeName || !types.TryGetValue(typeName, out var type))
            {
                issues.Add(new ImportIssue(row.Line, name, "Üyelik türü boş veya geçersiz."));
                continue;
            }

            var since = ImportParsing.Date(row.Get("Member Since", "Join Date"));
            var start = ImportParsing.Date(row.Get("Membership Start Date", "Start Date")) ?? since;
            if (start is null)
            {
                issues.Add(new ImportIssue(row.Line, name, "Başlangıç tarihi okunamadı."));
                continue;
            }

            var end = ImportParsing.Date(row.Get("Membership End Date", "End Date"));
            if (end < start)
            {
                issues.Add(new ImportIssue(row.Line, name, "Bitiş tarihi başlangıçtan önce."));
                continue;
            }

            var status = row.Get("Membership Status", "Status");
            var cancelled = status?.ToLower(Turkish) is "cancelled" or "deceased" or "iptal" or "vefat";
            DateOnly? cancelledOn = cancelled ? (end is { } e && e < today ? e : today) : null;
            var member = Member.Imported(contactId, type, await db.NextMemberNumberAsync(cancellationToken), since ?? start.Value, start.Value, end,
                cancelledOn, cancelled ? status : null, string.Join(" · ", new[] { "CiviCRM'den aktarıldı", row.Get("Source", "Membership Source") }.OfType<string>()), key);
            batch.Add((member, type));
            created++;
            if (batch.Count == BatchSize)
            {
                await memberships.SaveImportedAsync(batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await memberships.SaveImportedAsync(batch, cancellationToken);
        }

        return new ImportOutcome(created, 0, skipped, issues);
    }

    private static string? Key(ImportRow row)
    {
        if (row.Get("Membership ID", "Internal Membership ID") is { } id)
        {
            return ExternalKeys.For(SourceKey, "membership-" + id);
        }

        // Kimlik sütunu yoksa kişi + tür + başlangıç bir üyeliği tanımlar.
        return row.Get("Contact ID") is { } contact && row.Get("Membership Type") is { } type
            ? ExternalKeys.For(SourceKey, $"membership-{contact}-{type}-{row.Get("Membership Start Date", "Start Date", "Member Since", "Join Date")}")
            : null;
    }

    private async Task<Dictionary<string, MembershipType>> TypesAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        var comparer = StringComparer.Create(Turkish, ignoreCase: true);
        var types = (await db.Types.ToListAsync(cancellationToken)).GroupBy(t => t.Name, comparer).ToDictionary(g => g.Key, g => g.First(), comparer);
        var added = false;
        foreach (var name in rows.Select(r => r.Get("Membership Type")).OfType<string>().Distinct(comparer).Where(n => !types.ContainsKey(n)))
        {
            if (MembershipType.Create(name, "CiviCRM'den aktarıldı; süre ve ücreti kontrol edin.", 12, 0, 0) is { IsSuccess: true } type)
            {
                db.Types.Add(type.Value!);
                types[name] = type.Value!;
                added = true;
            }
        }

        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return types;
    }
}
