namespace Arma.Modules.Contacts.Domain;

public enum AddressKind
{
    Home = 1,
    Work = 2,
    Billing = 3,
    Other = 9,
}

/// <summary>Posta adresi. Kişi kaydında JSON kolon olarak saklanır.</summary>
public sealed record Address
{
    public AddressKind Kind { get; init; } = AddressKind.Home;

    /// <summary>Mahalle, cadde, sokak, bina ve daire.</summary>
    public string? Line { get; init; }

    /// <summary>İlçe.</summary>
    public string? District { get; init; }

    /// <summary>İl.</summary>
    public string? City { get; init; }

    public string? PostalCode { get; init; }

    public string Country { get; init; } = "TR";

    public bool IsPrimary { get; init; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(Line) && string.IsNullOrWhiteSpace(District) && string.IsNullOrWhiteSpace(City);

    public override string ToString() =>
        string.Join(", ", new[] { Line, $"{PostalCode} {District}".Trim(), City }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public static string KindLabel(AddressKind kind) => kind switch
    {
        AddressKind.Home => "Ev",
        AddressKind.Work => "İş",
        AddressKind.Billing => "Fatura",
        _ => "Diğer",
    };
}
