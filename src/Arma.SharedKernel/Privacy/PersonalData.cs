namespace Arma.SharedKernel.Privacy;

/// <summary>Kişisel veri envanterinin (VERBİS) bir satırı: hangi veri, hangi amaçla, hangi hukuki sebeple, ne kadar süre tutulur.</summary>
public sealed record PersonalDataItem(string Module, string Data, string Category, string Purpose, string LegalBasis, string Retention);

/// <summary>Bir kişinin bir modüldeki verisi; ilgili kişi başvurusunda dışa aktarılır.</summary>
public sealed record PersonalDataSection(string Title, IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows);

/// <summary>
/// Kişi verisi tutan her modül bunu uygular. KVKK modülü dışa aktarma ve anonimleştirmeyi
/// modüllere bu arayüz üzerinden yaptırır; böylece yeni modül eklendiğinde başvuru akışı değişmez.
/// </summary>
public interface IPersonalDataSource
{
    /// <summary>Anonimleştirme sırası: kişi kaydı en son silinir, diğer modüller önce.</summary>
    int Order { get; }

    IReadOnlyList<PersonalDataItem> Inventory { get; }

    Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Kişiyi tanımlayan veriyi kalıcı olarak siler veya anonimleştirir. Kanunen saklanması gereken kayıtlar
    /// (bağış tutarı, makbuz) kalır, yalnızca kişiyle bağı kopar. Denetim izine değer yazmaz.
    /// </summary>
    Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken);
}
