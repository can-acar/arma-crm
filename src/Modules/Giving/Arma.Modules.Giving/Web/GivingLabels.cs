using Arma.Modules.Giving.Domain;
using Arma.UI;

namespace Arma.Modules.Giving.Web;

public static class GivingLabels
{
    /// <summary>Elle kayıtta seçilebilen yöntemler; çevrim içi ödeme yalnızca bağış formundan gelir.</summary>
    public static readonly PaymentMethod[] ManualMethods =
        [PaymentMethod.BankTransfer, PaymentMethod.Cash, PaymentMethod.CreditCard, PaymentMethod.Check, PaymentMethod.InKind, PaymentMethod.Other];

    public static readonly string[] Currencies = ["TRY", "USD", "EUR", "GBP"];

    public static string Method(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Nakit",
        PaymentMethod.BankTransfer => "Havale / EFT",
        PaymentMethod.CreditCard => "Kredi kartı (POS)",
        PaymentMethod.Online => "Çevrim içi",
        PaymentMethod.Check => "Çek",
        PaymentMethod.InKind => "Ayni bağış",
        _ => "Diğer",
    };

    public static string Status(DonationStatus status) => status switch
    {
        DonationStatus.Pending => "Ödeme bekleniyor",
        DonationStatus.Completed => "Tamamlandı",
        DonationStatus.Failed => "Başarısız",
        DonationStatus.Refunded => "İade edildi",
        _ => "İptal",
    };

    public static BadgeVariant StatusVariant(DonationStatus status) => status switch
    {
        DonationStatus.Completed => BadgeVariant.Secondary,
        DonationStatus.Failed or DonationStatus.Refunded => BadgeVariant.Destructive,
        _ => BadgeVariant.Outline,
    };

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy");
}
