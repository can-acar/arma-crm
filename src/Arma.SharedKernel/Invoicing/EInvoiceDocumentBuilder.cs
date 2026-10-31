using System.Text.RegularExpressions;

namespace Arma.SharedKernel.Invoicing;

/// <summary>
/// Faturayı adım adım kurar (kurucu) ve <see cref="Build"/> sırasında bütün kuralları birlikte denetler:
/// <code>
/// var document = EInvoiceDocumentBuilder.Create("ARM2026000000001", uuid)
///     .Profile(EInvoiceProfiles.EArsiv).IssuedOn(today, now)
///     .Seller(seller).Buyer(buyer)
///     .Line("Danışmanlık", 2, "saat", 1500m, vatRate: 20)
///     .Build();
/// </code>
/// </summary>
public sealed partial class EInvoiceDocumentBuilder
{
    private readonly List<EInvoiceLine> lines = [];
    private readonly List<string> notes = [];
    private readonly string number;
    private readonly Guid uuid;
    private EInvoiceProfiles profile = EInvoiceProfiles.EArsiv;
    private DateOnly issuedOn;
    private TimeOnly issuedAt;
    private EInvoiceParty? seller, buyer;
    private string currency = "TRY";
    private string? exemptionCode, exemptionReason, orderReference;

    private EInvoiceDocumentBuilder(string number, Guid uuid) => (this.number, this.uuid) = (number?.Trim() ?? "", uuid);

    public static EInvoiceDocumentBuilder Create(string number, Guid uuid) => new(number, uuid);

    public EInvoiceDocumentBuilder Profile(EInvoiceProfiles value)
    {
        profile = value;
        return this;
    }

    public EInvoiceDocumentBuilder IssuedOn(DateOnly date, TimeOnly time)
    {
        (issuedOn, issuedAt) = (date, new TimeOnly(time.Hour, time.Minute, time.Second));
        return this;
    }

    public EInvoiceDocumentBuilder Currency(string value)
    {
        currency = value;
        return this;
    }

    public EInvoiceDocumentBuilder Seller(EInvoiceParty value)
    {
        seller = value;
        return this;
    }

    public EInvoiceDocumentBuilder Buyer(EInvoiceParty value)
    {
        buyer = value;
        return this;
    }

    public EInvoiceDocumentBuilder Line(string description, decimal quantity, string? unit, decimal unitPrice, decimal discountRate = 0, int vatRate = 20)
    {
        lines.Add(new EInvoiceLine(lines.Count + 1, description?.Trim() ?? "", quantity, UnitCodes.From(unit), EInvoiceMath.Round(unitPrice), discountRate, vatRate));
        return this;
    }

    public EInvoiceDocumentBuilder Note(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            notes.Add(value.Trim());
        }

        return this;
    }

    public EInvoiceDocumentBuilder Exemption(string? code, string? reason)
    {
        (exemptionCode, exemptionReason) = (string.IsNullOrWhiteSpace(code) ? null : code.Trim(), string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        return this;
    }

    public EInvoiceDocumentBuilder OrderReference(string? value)
    {
        orderReference = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return this;
    }

    /// <summary>Kurallara uymayan her alanı tek mesajda listeler.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!NumberPattern().IsMatch(number) || (issuedOn != default && number.Substring(3, 4) != issuedOn.Year.ToString("D4")))
        {
            problems.Add("fatura numarası (3 karakter ön ek, fatura yılı ve 9 hane sıra numarası)");
        }

        if (uuid == Guid.Empty)
        {
            problems.Add("ETTN");
        }

        if (profile is not (EInvoiceProfiles.EArsiv or EInvoiceProfiles.EFatura))
        {
            problems.Add("senaryo");
        }

        if (issuedOn == default)
        {
            problems.Add("fatura tarihi");
        }

        if (currency != "TRY")
        {
            problems.Add("para birimi (yalnızca TL)");
        }

        if (seller is null || string.IsNullOrWhiteSpace(seller.Name) || !TaxIds.IsValid(seller.TaxId))
        {
            problems.Add("satıcı adı ve geçerli VKN/TCKN");
        }
        else if (string.IsNullOrWhiteSpace(seller.TaxOffice) || string.IsNullOrWhiteSpace(seller.City))
        {
            problems.Add("satıcı vergi dairesi ve il");
        }

        if (buyer is null || string.IsNullOrWhiteSpace(buyer.Name))
        {
            problems.Add("alıcı adı");
        }
        else if (!(TaxIds.IsValid(buyer.TaxId) || (profile == EInvoiceProfiles.EArsiv && buyer.TaxId == TaxIds.Anonymous)))
        {
            problems.Add(profile == EInvoiceProfiles.EFatura ? "alıcının geçerli VKN/TCKN'si (e-Fatura)" : "alıcının geçerli VKN/TCKN'si");
        }
        else if (string.IsNullOrWhiteSpace(buyer.City))
        {
            problems.Add("alıcının ili");
        }

        if (lines.Count == 0)
        {
            problems.Add("en az bir kalem");
        }

        foreach (var line in lines)
        {
            if (line.Description.Length is 0 or > 500 || line.Quantity <= 0 || line.UnitPrice < 0 || line.DiscountRate is < 0 or > 100 || line.VatRate is < 0 or > 100)
            {
                problems.Add($"{line.Position}. kalem");
            }
        }

        if (lines.Any(l => l.VatRate == 0) && exemptionCode is null)
        {
            problems.Add("%0 KDV'li kalem için istisna kodu");
        }

        if (lines.Count > 0 && lines.Sum(l => l.Net) <= 0)
        {
            problems.Add("fatura tutarı");
        }

        return problems;
    }

    /// <exception cref="InvalidOperationException">Eksik veya hatalı alan varsa; mesaj hepsini listeler.</exception>
    public EInvoiceDocument Build()
    {
        var problems = Problems();
        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Fatura eksik veya hatalı: " + string.Join(", ", problems) + ".");
        }

        var document = new EInvoiceDocument
        {
            Number = number,
            Uuid = uuid,
            Profile = profile,
            IssuedOn = issuedOn,
            IssuedAt = issuedAt,
            Currency = currency,
            Seller = seller!,
            Buyer = buyer!,
            Lines = [.. lines],
            Notes = [.. notes],
            ExemptionCode = lines.Any(l => l.VatRate == 0) ? exemptionCode : null,
            ExemptionReason = lines.Any(l => l.VatRate == 0) ? exemptionReason : null,
            OrderReference = orderReference,
        };
        document.Xml = UblTrWriter.Write(document);
        return document;
    }

    [GeneratedRegex("^[A-Z0-9]{3}[0-9]{13}$")]
    private static partial Regex NumberPattern();
}
