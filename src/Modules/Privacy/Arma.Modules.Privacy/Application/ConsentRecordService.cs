using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Privacy.Domain;
using Arma.Modules.Privacy.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Privacy.Application;

/// <summary>Kişinin bir metne göre güncel rıza durumu. <see cref="Outdated"/>: rıza eski bir sürüme verilmiş.</summary>
public sealed record ConsentState(Guid NoticeId, string Title, string Slug, int CurrentVersion, bool? Granted, int? VersionNumber, DateTimeOffset? RecordedAt, string? Source)
{
    public bool Outdated => Granted == true && VersionNumber < CurrentVersion;
}

public sealed record ConsentHistoryItem(Guid Id, string Title, int VersionNumber, bool Granted, string Source, string? IpAddress, string? Note, DateTimeOffset RecordedAt, string? RecordedBy);

/// <summary>Açık rıza kayıtları: yalnızca eklenir, değiştirilmez.</summary>
public sealed class ConsentRecordService(PrivacyDbContext db, IContactDirectory contacts, TimeProvider clock)
{
    public async Task<IReadOnlyList<ConsentState>> ForContactAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var notices = await db.Notices.AsNoTracking().Where(n => n.RequiresConsent && n.CurrentVersion > 0).OrderBy(n => n.Title).ToListAsync(cancellationToken);
        var latest = await db.ConsentRecords.AsNoTracking().Where(c => c.ContactId == contactId)
            .GroupBy(c => c.NoticeId)
            .Select(g => g.OrderByDescending(c => c.RecordedAt).First())
            .ToListAsync(cancellationToken);
        return notices.Select(n => latest.FirstOrDefault(c => c.NoticeId == n.Id) is { } c
                ? new ConsentState(n.Id, n.Title, n.Slug, n.CurrentVersion, c.Granted, c.VersionNumber, c.RecordedAt, c.Source)
                : new ConsentState(n.Id, n.Title, n.Slug, n.CurrentVersion, null, null, null, null))
            .ToList();
    }

    public async Task<IReadOnlyList<ConsentHistoryItem>> HistoryAsync(Guid contactId, CancellationToken cancellationToken = default) =>
        await db.ConsentRecords.AsNoTracking().Where(c => c.ContactId == contactId)
            .Join(db.Notices.IgnoreQueryFilters(), c => c.NoticeId, n => n.Id, (c, n) => new { c, n.Title })
            .OrderByDescending(x => x.c.RecordedAt)
            .Select(x => new ConsentHistoryItem(x.c.Id, x.Title, x.c.VersionNumber, x.c.Granted, x.c.Source, x.c.IpAddress, x.c.Note, x.c.RecordedAt, x.c.CreatedBy))
            .ToListAsync(cancellationToken);

    internal async Task RecordBySlugAsync(Guid contactId, string slug, bool granted, string source, string? ipAddress, CancellationToken cancellationToken)
    {
        var noticeId = await db.Notices.Where(n => n.Slug == slug).Select(n => (Guid?)n.Id).FirstOrDefaultAsync(cancellationToken);
        if (noticeId is { } id)
        {
            await RecordAsync(contactId, id, granted, source, ipAddress, null, cancellationToken);
        }
    }

    /// <summary>Metnin yayımlanmış son sürümüne rıza verildiğini veya geri alındığını kaydeder.</summary>
    public async Task<Result<Guid>> RecordAsync(Guid contactId, Guid noticeId, bool granted, string source, string? ipAddress, string? note, CancellationToken cancellationToken = default)
    {
        if (await contacts.FindAsync(contactId, cancellationToken) is null)
        {
            return PrivacyErrors.ContactNotFound;
        }

        var notice = await db.Notices.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noticeId, cancellationToken);
        if (notice is null)
        {
            return PrivacyErrors.NoticeNotFound;
        }

        var version = await db.NoticeVersions.FirstOrDefaultAsync(v => v.NoticeId == noticeId && v.Number == notice.CurrentVersion, cancellationToken);
        if (version is null || !notice.RequiresConsent)
        {
            return new Error("consents.not_published", "Bu metin açık rıza metni olarak yayımlanmamış.");
        }

        var record = ConsentRecord.Create(contactId, version, granted, source, ipAddress, note, clock.GetUtcNow());
        db.ConsentRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record.Id;
    }
}
