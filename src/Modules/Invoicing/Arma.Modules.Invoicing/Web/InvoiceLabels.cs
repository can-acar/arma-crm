using Arma.Modules.Invoicing.Contracts;
using Arma.Modules.Invoicing.Domain;
using Arma.SharedKernel.Invoicing;
using Arma.UI;

namespace Arma.Modules.Invoicing.Web;

public static class InvoiceLabels
{
    public static string Status(InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => "Taslak",
        InvoiceStatus.Issued => "Kesildi",
        _ => "İptal edildi",
    };

    public static BadgeVariant StatusVariant(InvoiceStatus status, bool transmitted, bool failed) => status switch
    {
        InvoiceStatus.Draft when failed => BadgeVariant.Destructive,
        InvoiceStatus.Draft => BadgeVariant.Secondary,
        InvoiceStatus.Issued when !transmitted => BadgeVariant.Outline,
        InvoiceStatus.Issued => BadgeVariant.Default,
        _ => BadgeVariant.Outline,
    };

    public static string Profile(EInvoiceProfiles profile) => profile == EInvoiceProfiles.EFatura ? "e-Fatura" : "e-Arşiv";

    public static string Profiles(EInvoiceProfiles profiles) =>
        string.Join(", ", new[] { EInvoiceProfiles.EArsiv, EInvoiceProfiles.EFatura }.Where(p => profiles.HasFlag(p)).Select(Profile));

    public static string Source(string kind) => kind switch
    {
        InvoiceSources.Quote => "Teklif",
        InvoiceSources.Dues => "Aidat",
        _ => "Elle",
    };
}
