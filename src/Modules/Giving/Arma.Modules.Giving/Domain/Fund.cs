using Arma.SharedKernel.Domain;

namespace Arma.Modules.Giving.Domain;

/// <summary>Bağışın yönlendirildiği amaç: genel bağış, burs fonu, kurban gibi.</summary>
public sealed class Fund : Entity
{
    private Fund()
    {
    }

    public string Name { get; private set; } = "";

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    /// <summary>Çevrim içi bağış formunda seçilebilir mi.</summary>
    public bool IsPublic { get; private set; }

    public static Result<Fund> Create(string name, string? description, bool isPublic)
    {
        var fund = new Fund();
        var result = fund.Update(name, description, true, isPublic);
        return result.IsSuccess ? fund : result.Error!;
    }

    public Result<bool> Update(string name, string? description, bool isActive, bool isPublic)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 120)
        {
            return new Error("funds.name", "Fon adı 1-120 karakter olmalı.");
        }

        Name = name;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = isActive;
        IsPublic = isPublic;
        return true;
    }
}

/// <summary>Makbuzda yazan kurum bilgileri; kurum başına tek kayıt.</summary>
public sealed class ReceiptSettings : Entity
{
    public string OrganizationName { get; set; } = "";

    public string? Address { get; set; }

    /// <summary>Vergi dairesi ve numarası, dernek kütük numarası gibi bilgiler.</summary>
    public string? LegalInfo { get; set; }

    /// <summary>Makbuz altındaki açıklama (vergi indirimi notu, teşekkür).</summary>
    public string? Footer { get; set; }

    /// <summary>Makbuz numarası öneki; numara "{önek}{yıl}-{sıra}" biçimindedir.</summary>
    public string NumberPrefix { get; set; } = "";

    /// <summary>
    /// Bağış formunda sunulan ödeme sağlayıcılarının anahtarları, sırasıyla ("iyzico,paytr"). Boşsa yapılandırılmış
    /// bütün sağlayıcılar sunulur.
    /// </summary>
    public string? PaymentProviders { get; set; }
}
