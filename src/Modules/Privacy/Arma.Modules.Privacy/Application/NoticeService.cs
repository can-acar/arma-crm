using Arma.Modules.Privacy.Domain;
using Arma.Modules.Privacy.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Privacy.Application;

public sealed record NoticeView(Guid Id, string Title, string Slug, bool RequiresConsent, string DraftText, int CurrentVersion, DateTimeOffset? PublishedAt, bool HasUnpublishedChanges);

public sealed record NoticeVersionView(Guid Id, Guid NoticeId, int Number, string Title, string Text, bool RequiresConsent, DateTimeOffset PublishedAt);

public sealed record SaveNoticeRequest(string Title, string Slug, bool RequiresConsent, string Text);

/// <summary>Aydınlatma ve açık rıza metinleri.</summary>
public sealed class NoticeService(PrivacyDbContext db, TimeProvider clock)
{
    public async Task<IReadOnlyList<NoticeView>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.Notices.AsNoTracking().OrderBy(n => n.Title)
            .Select(n => new NoticeView(n.Id, n.Title, n.Slug, n.RequiresConsent, n.DraftText, n.CurrentVersion, n.PublishedAt, n.HasUnpublishedChanges))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NoticeVersionView>> VersionsAsync(Guid noticeId, CancellationToken cancellationToken = default) =>
        await db.NoticeVersions.AsNoTracking().Where(v => v.NoticeId == noticeId).OrderByDescending(v => v.Number)
            .Select(v => new NoticeVersionView(v.Id, v.NoticeId, v.Number, v.Title, v.Text, v.RequiresConsent, v.PublishedAt))
            .ToListAsync(cancellationToken);

    /// <summary>Kamuya açık sayfada gösterilen, yayımlanmış son sürüm.</summary>
    public async Task<NoticeVersionView?> PublishedAsync(string slug, CancellationToken cancellationToken = default) =>
        await db.Notices.AsNoTracking().Where(n => n.Slug == slug && n.CurrentVersion > 0)
            .Join(db.NoticeVersions, n => new { n.Id, Number = n.CurrentVersion }, v => new { Id = v.NoticeId, v.Number }, (n, v) => v)
            .Select(v => new NoticeVersionView(v.Id, v.NoticeId, v.Number, v.Title, v.Text, v.RequiresConsent, v.PublishedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<Result<Guid>> SaveAsync(Guid? id, SaveNoticeRequest request, CancellationToken cancellationToken = default)
    {
        Notice? notice;
        if (id is null)
        {
            var created = Notice.Create(request.Title, request.Slug, request.RequiresConsent, request.Text);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            notice = created.Value!;
            db.Notices.Add(notice);
        }
        else
        {
            notice = await db.Notices.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
            if (notice is null)
            {
                return PrivacyErrors.NoticeNotFound;
            }

            var updated = notice.Update(request.Title, request.Slug, request.RequiresConsent, request.Text);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }
        }

        if (await db.Notices.AnyAsync(n => n.Slug == notice.Slug && n.Id != notice.Id, cancellationToken))
        {
            return new Error("notices.slug_taken", "Bu adresle başka bir metin var.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return notice.Id;
    }

    /// <summary>Taslağı yeni sürüm olarak yayımlar. Önceki sürümlere verilmiş rızalar o sürüme bağlı kalır.</summary>
    public async Task<Result<int>> PublishAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var notice = await db.Notices.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notice is null)
        {
            return PrivacyErrors.NoticeNotFound;
        }

        var version = notice.Publish(clock.GetUtcNow());
        if (!version.IsSuccess)
        {
            return version.Error!;
        }

        db.NoticeVersions.Add(version.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return version.Value!.Number;
    }
}

public static class PrivacyErrors
{
    public static readonly Error NoticeNotFound = new("notices.not_found", "Metin bulunamadı.");

    public static readonly Error RequestNotFound = new("requests.not_found", "Başvuru bulunamadı.");

    public static readonly Error ContactNotFound = new("privacy.contact_not_found", "Kişi bulunamadı.");
}
