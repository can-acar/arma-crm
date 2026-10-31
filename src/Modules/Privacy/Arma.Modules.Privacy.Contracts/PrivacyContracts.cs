namespace Arma.Modules.Privacy.Contracts;

/// <summary>Kişi bir açık rıza metnini onayladı (web formu gibi). Metnin yayımlanmış son sürümüne bağlanır.</summary>
public sealed record RecordExplicitConsent(Guid ContactId, string NoticeSlug, bool Granted, string Source, string? IpAddress);

public sealed record NoticeOption(string Slug, string Title, bool RequiresConsent);

/// <summary>Formlarda seçilebilecek yayımlanmış aydınlatma ve açık rıza metinleri.</summary>
public interface INoticeDirectory
{
    Task<IReadOnlyList<NoticeOption>> PublishedAsync(CancellationToken cancellationToken = default);
}
