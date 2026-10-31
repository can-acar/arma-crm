using System.Globalization;
using System.Xml.Linq;

namespace Arma.SharedKernel.Invoicing;

/// <summary>
/// Faturayı UBL-TR 1.2 XML'ine yazar. Çıktı imzasızdır: mali mühür veya e-imza entegratörde ya da GİB portalında eklenir.
/// Kalem tutarı (LineExtensionAmount) iskonto düşülmüş net tutardır; iskonto kalemde AllowanceCharge olarak yazılır.
/// </summary>
public static class UblTrWriter
{
    public static readonly XNamespace Inv = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    public static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    public static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    public static readonly XNamespace Ext = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";

    public static string Write(EInvoiceDocument d)
    {
        var currency = d.Currency;
        var root = new XElement(Inv + "Invoice",
            new XAttribute(XNamespace.Xmlns + "cac", Cac),
            new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
            new XAttribute(XNamespace.Xmlns + "ext", Ext),
            new XElement(Ext + "UBLExtensions", new XElement(Ext + "UBLExtension", new XElement(Ext + "ExtensionContent"))),
            Cb("UBLVersionID", "2.1"),
            Cb("CustomizationID", "TR1.2"),
            Cb("ProfileID", d.ProfileId),
            Cb("ID", d.Number),
            Cb("CopyIndicator", "false"),
            Cb("UUID", d.Uuid.ToString().ToUpperInvariant()),
            Cb("IssueDate", d.IssuedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Cb("IssueTime", d.IssuedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            Cb("InvoiceTypeCode", d.TypeCode),
            d.Notes.Select(n => Cb("Note", n)),
            Cb("DocumentCurrencyCode", currency),
            Cb("LineCountNumeric", d.Lines.Count.ToString(CultureInfo.InvariantCulture)),
            d.OrderReference is { } order
                ? new XElement(Cac + "OrderReference", Cb("ID", order), Cb("IssueDate", d.IssuedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                : null,
            new XElement(Cac + "Signature",
                new XElement(Cbc + "ID", new XAttribute("schemeID", "VKN_TCKN"), d.Seller.TaxId),
                new XElement(Cac + "SignatoryParty", Identification(d.Seller), Address(d.Seller)),
                new XElement(Cac + "DigitalSignatureAttachment", new XElement(Cac + "ExternalReference", Cb("URI", "#Signature")))),
            new XElement(Cac + "AccountingSupplierParty", Party(d.Seller)),
            new XElement(Cac + "AccountingCustomerParty", Party(d.Buyer)),
            TaxTotal(d.TaxSubtotals, d.VatTotal, currency, d),
            new XElement(Cac + "LegalMonetaryTotal",
                Amount("LineExtensionAmount", d.TaxExclusive, currency),
                Amount("TaxExclusiveAmount", d.TaxExclusive, currency),
                Amount("TaxInclusiveAmount", d.Payable, currency),
                Amount("AllowanceTotalAmount", 0, currency),
                Amount("PayableAmount", d.Payable, currency)),
            d.Lines.Select(l => Line(l, currency, d)));

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root).Declaration + Environment.NewLine + root;
    }

    private static XElement Line(EInvoiceLine line, string currency, EInvoiceDocument d) =>
        new(Cac + "InvoiceLine",
            Cb("ID", line.Position.ToString(CultureInfo.InvariantCulture)),
            new XElement(Cbc + "InvoicedQuantity", new XAttribute("unitCode", line.UnitCode), line.Quantity.ToString("0.####", CultureInfo.InvariantCulture)),
            Amount("LineExtensionAmount", line.Net, currency),
            line.Discount > 0
                ? new XElement(Cac + "AllowanceCharge",
                    Cb("ChargeIndicator", "false"),
                    Cb("MultiplierFactorNumeric", (line.DiscountRate / 100m).ToString("0.####", CultureInfo.InvariantCulture)),
                    Amount("Amount", line.Discount, currency),
                    Amount("BaseAmount", line.Gross, currency))
                : null,
            TaxTotal([new EInvoiceTaxSubtotal(line.VatRate, line.Net, line.Vat)], line.Vat, currency, d),
            new XElement(Cac + "Item", Cb("Name", line.Description)),
            new XElement(Cac + "Price", Amount("PriceAmount", line.UnitPrice, currency)));

    private static XElement TaxTotal(IEnumerable<EInvoiceTaxSubtotal> subtotals, decimal total, string currency, EInvoiceDocument d) =>
        new(Cac + "TaxTotal",
            Amount("TaxAmount", total, currency),
            subtotals.Select(s => new XElement(Cac + "TaxSubtotal",
                Amount("TaxableAmount", s.Base, currency),
                Amount("TaxAmount", s.Amount, currency),
                Cb("Percent", s.VatRate.ToString(CultureInfo.InvariantCulture)),
                new XElement(Cac + "TaxCategory",
                    s.VatRate == 0 && d.ExemptionCode is { } code ? Cb("TaxExemptionReasonCode", code) : null,
                    s.VatRate == 0 && d.ExemptionReason is { } reason ? Cb("TaxExemptionReason", reason) : null,
                    new XElement(Cac + "TaxScheme", Cb("Name", "KDV"), Cb("TaxTypeCode", "0015"))))));

    private static XElement Party(EInvoiceParty p) =>
        new(Cac + "Party",
            p.Website is { Length: > 0 } web ? Cb("WebsiteURI", web) : null,
            Identification(p),
            new XElement(Cac + "PartyName", Cb("Name", p.Name)),
            Address(p),
            p.TaxOffice is { Length: > 0 } office ? new XElement(Cac + "PartyTaxScheme", new XElement(Cac + "TaxScheme", Cb("Name", office))) : null,
            p.Phone is { Length: > 0 } || p.Email is { Length: > 0 }
                ? new XElement(Cac + "Contact",
                    p.Phone is { Length: > 0 } phone ? Cb("Telephone", phone) : null,
                    p.Email is { Length: > 0 } email ? Cb("ElectronicMail", email) : null)
                : null,
            p.IsPerson ? new XElement(Cac + "Person", Cb("FirstName", p.FirstName ?? FirstName(p.Name)), Cb("FamilyName", p.LastName ?? LastName(p.Name))) : null);

    private static XElement Identification(EInvoiceParty p) =>
        new(Cac + "PartyIdentification", new XElement(Cbc + "ID", new XAttribute("schemeID", p.IsPerson ? "TCKN" : "VKN"), p.TaxId));

    private static XElement Address(EInvoiceParty p) =>
        new(Cac + "PostalAddress",
            p.Street is { Length: > 0 } street ? Cb("StreetName", street) : null,
            Cb("CitySubdivisionName", p.District ?? p.City ?? ""),
            Cb("CityName", p.City ?? ""),
            p.PostalCode is { Length: > 0 } zip ? Cb("PostalZone", zip) : null,
            new XElement(Cac + "Country", Cb("Name", p.Country)));

    private static string FirstName(string name) => name.Contains(' ') ? name[..name.LastIndexOf(' ')] : name;

    private static string LastName(string name) => name.Contains(' ') ? name[(name.LastIndexOf(' ') + 1)..] : name;

    private static XElement Cb(string name, string value) => new(Cbc + name, value);

    private static XElement Amount(string name, decimal value, string currency) =>
        new(Cbc + name, new XAttribute("currencyID", currency), value.ToString("0.00", CultureInfo.InvariantCulture));
}
