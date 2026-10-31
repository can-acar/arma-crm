namespace Arma.SharedKernel.Invoicing;

/// <summary>Faturadaki taraf (satıcı veya alıcı). 10 haneli kimlik VKN, 11 haneli kimlik TCKN'dir.</summary>
public sealed record EInvoiceParty(string Name, string TaxId)
{
    public string? TaxOffice { get; init; }

    /// <summary>Kişi alıcıda ad ve soyad ayrı yazılır (TCKN ile).</summary>
    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    /// <summary>Mahalle, cadde, sokak, bina ve daire.</summary>
    public string? Street { get; init; }

    /// <summary>İlçe.</summary>
    public string? District { get; init; }

    /// <summary>İl.</summary>
    public string? City { get; init; }

    public string? PostalCode { get; init; }

    public string Country { get; init; } = "Türkiye";

    public string? Email { get; init; }

    public string? Phone { get; init; }

    public string? Website { get; init; }

    public bool IsPerson => TaxId.Length == 11;
}

/// <summary>Fatura kalemi. Tutarlar kuruşa yuvarlanır; iskonto KDV'den önce uygulanır.</summary>
public sealed record EInvoiceLine(int Position, string Description, decimal Quantity, string UnitCode, decimal UnitPrice, decimal DiscountRate, int VatRate)
{
    public decimal Gross => EInvoiceMath.Round(Quantity * UnitPrice);

    public decimal Discount => EInvoiceMath.Round(Gross * DiscountRate / 100m);

    public decimal Net => Gross - Discount;

    public decimal Vat => EInvoiceMath.Round(Net * VatRate / 100m);
}

/// <summary>Bir KDV oranının toplamı.</summary>
public sealed record EInvoiceTaxSubtotal(int VatRate, decimal Base, decimal Amount);

/// <summary>
/// Gönderilmeye hazır, doğrulanmış fatura. <see cref="EInvoiceDocumentBuilder"/> ile kurulur; kurucu tutarları hesaplar,
/// kuralları denetler ve UBL-TR 1.2 XML'ini üretir.
/// </summary>
public sealed class EInvoiceDocument
{
    internal EInvoiceDocument()
    {
    }

    /// <summary>GİB biçiminde fatura numarası: 3 karakter ön ek, 4 hane yıl, 9 hane sıra (ARM2026000000001).</summary>
    public required string Number { get; init; }

    /// <summary>Faturanın evrensel kimliği (ETTN).</summary>
    public required Guid Uuid { get; init; }

    public required EInvoiceProfiles Profile { get; init; }

    public required DateOnly IssuedOn { get; init; }

    public required TimeOnly IssuedAt { get; init; }

    public string Currency { get; init; } = "TRY";

    public required EInvoiceParty Seller { get; init; }

    public required EInvoiceParty Buyer { get; init; }

    public required IReadOnlyList<EInvoiceLine> Lines { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>%0 KDV'li kalem varsa GİB istisna kodu (ör. 351) ve açıklaması.</summary>
    public string? ExemptionCode { get; init; }

    public string? ExemptionReason { get; init; }

    /// <summary>Faturayı oluşturan kayıt (teklif numarası gibi); sipariş referansı olarak yazılır.</summary>
    public string? OrderReference { get; init; }

    /// <summary>UBL-TR 1.2 biçiminde imzasız fatura.</summary>
    public string Xml { get; internal set; } = "";

    /// <summary>GİB profil kimliği.</summary>
    public string ProfileId => Profile == EInvoiceProfiles.EFatura ? "TEMELFATURA" : "EARSIVFATURA";

    /// <summary>Fatura tipi: KDV'si olmayan istisna kodlu fatura ISTISNA, diğerleri SATIS.</summary>
    public string TypeCode => VatTotal == 0 && ExemptionCode is { } code && code.StartsWith('3') && code != "351" ? "ISTISNA" : "SATIS";

    public decimal LineExtension => Lines.Sum(l => l.Gross);

    public decimal DiscountTotal => Lines.Sum(l => l.Discount);

    public decimal TaxExclusive => Lines.Sum(l => l.Net);

    public decimal VatTotal => Lines.Sum(l => l.Vat);

    public decimal Payable => TaxExclusive + VatTotal;

    public IReadOnlyList<EInvoiceTaxSubtotal> TaxSubtotals =>
        [.. Lines.GroupBy(l => l.VatRate).OrderBy(g => g.Key).Select(g => new EInvoiceTaxSubtotal(g.Key, g.Sum(l => l.Net), g.Sum(l => l.Vat)))];
}

public static class EInvoiceMath
{
    public static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}

/// <summary>Vergi kimlik numarası (VKN, 10 hane) ve T.C. kimlik numarası (TCKN, 11 hane) kontrolleri.</summary>
public static class TaxIds
{
    /// <summary>Kimliği bilinmeyen kişi alıcı için e-Arşiv faturada kullanılan numara.</summary>
    public const string Anonymous = "11111111111";

    public static bool IsValid(string? value) => IsValidVkn(value) || IsValidTckn(value);

    public static bool IsValidVkn(string? value)
    {
        if (value is not { Length: 10 } || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < 9; i++)
        {
            var shifted = (value[i] - '0' + 9 - i) % 10;
            var weighted = shifted * (1 << (9 - i)) % 9;
            sum += shifted != 0 && weighted == 0 ? 9 : weighted;
        }

        return (10 - sum % 10) % 10 == value[9] - '0';
    }

    public static bool IsValidTckn(string? value)
    {
        if (value is not { Length: 11 } || !value.All(char.IsAsciiDigit) || value[0] == '0')
        {
            return false;
        }

        var d = value.Select(c => c - '0').ToArray();
        var tenth = ((d[0] + d[2] + d[4] + d[6] + d[8]) * 7 - (d[1] + d[3] + d[5] + d[7])) % 10;
        return d[9] == (tenth + 10) % 10 && d.Take(10).Sum() % 10 == d[10];
    }
}

/// <summary>Serbest yazılmış birimi (adet, saat, ay) UBL birim koduna çevirir.</summary>
public static class UnitCodes
{
    public static string From(string? unit) => unit?.Trim().ToLowerInvariant() switch
    {
        "saat" or "sa" => "HUR",
        "gün" or "gun" => "DAY",
        "ay" => "MON",
        "yıl" or "yil" => "ANN",
        "kg" or "kilogram" => "KGM",
        "g" or "gr" or "gram" => "GRM",
        "lt" or "l" or "litre" => "LTR",
        "m" or "metre" => "MTR",
        "m2" or "m²" => "MTK",
        "paket" => "PA",
        "kutu" => "BX",
        _ => "C62",
    };
}
