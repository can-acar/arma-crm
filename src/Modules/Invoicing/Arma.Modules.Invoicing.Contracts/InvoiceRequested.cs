namespace Arma.Modules.Invoicing.Contracts;

/// <summary>Faturayı isteyen kaynaklar. Aynı kaynak kaydı için ikinci fatura açılmaz.</summary>
public static class InvoiceSources
{
    /// <summary>Kabul edilen satış teklifi.</summary>
    public const string Quote = "sales.quote";

    /// <summary>Alınan üyelik aidatı.</summary>
    public const string Dues = "membership.dues";
}

/// <param name="VatRate">KDV oranı (%); boşsa fatura ayarlarındaki aidat KDV oranı kullanılır.</param>
public sealed record InvoiceRequestLine(string Description, decimal Quantity, string? Unit, decimal UnitPrice, decimal DiscountRate, int? VatRate)
{
    /// <summary>Birim fiyat KDV dahil (alınan aidat gibi); fatura KDV'yi içinden ayırır.</summary>
    public bool VatIncluded { get; init; }
}

/// <summary>
/// Bir modül, kaydı için fatura kesilmesini istiyor (kabul edilen teklif, alınan aidat). Fatura modülü ayarlarına göre
/// taslak fatura açar ya da faturayı hemen gönderir; kaynak türü kapalıysa isteği yok sayar.
/// </summary>
/// <param name="SourceLabel">Kaynağın kullanıcıya görünen adı (T2026-0004, "12 numaralı üye aidatı").</param>
public sealed record InvoiceRequested(string SourceKind, Guid SourceId, string? SourceLabel, Guid ContactId, DateOnly IssuedOn, IReadOnlyList<InvoiceRequestLine> Lines, string? Note);
