namespace Arma.Modules.Contacts.Contracts;

public enum CriterionKind
{
    /// <summary>Değer almaz: "e-posta izni onaylı".</summary>
    Flag,

    /// <summary>Tutar veya adet.</summary>
    Number,

    /// <summary>yyyy-MM-dd.</summary>
    Date,

    /// <summary>Gün sayısı: "90 gündür aktivite yok".</summary>
    Days,

    /// <summary><see cref="CriterionDefinition.Options"/> içinden biri.</summary>
    Choice,

    Text,
}

public sealed record CriterionOption(string Value, string Label);

public sealed record CriterionDefinition(string Key, string Group, string Label, CriterionKind Kind, IReadOnlyList<CriterionOption>? Options = null);

/// <summary>Ölçütün sonucu: bu kişiler dahil edilir ya da (<see cref="Exclude"/>) çıkarılır.</summary>
public sealed record CriterionMatch(IReadOnlyCollection<Guid> ContactIds, bool Exclude = false);

/// <summary>
/// Gelişmiş kişi aramasına başka modüllerin eklediği ölçütler (bağış yaptı, izni var, aktivite yok).
/// Kişiler modülü sonuçları kesiştirir; modül kendi tablosunu kendi sorgular.
/// </summary>
public interface IContactCriteriaProvider
{
    Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken);

    /// <summary>Bilinmeyen anahtar için null döner.</summary>
    Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken);
}
