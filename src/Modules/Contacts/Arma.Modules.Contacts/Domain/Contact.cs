using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Domain;

namespace Arma.Modules.Contacts.Domain;

/// <summary>Kişi, kurum veya hane. Bağış, üyelik ve aktivite kayıtları bu varlığa bağlanır.</summary>
public sealed class Contact : Entity
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private Contact()
    {
    }

    public ContactType Type { get; private set; }

    public string? FirstName { get; private set; }

    public string? LastName { get; private set; }

    public string? OrganizationName { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public string? Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>Kişi için TCKN, kurum için VKN. Ekranda yalnızca maskeli gösterilir.</summary>
    public string? NationalId { get; private set; }

    /// <summary>Türkçe kurallarla küçük harfe çevrilmiş ad ve e-posta; arama bu alan üzerinden yapılır.</summary>
    public string SearchText { get; private set; } = string.Empty;

    public static Result<Contact> CreatePerson(string firstName, string lastName, string? email, string? phone, string? tckn)
    {
        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            return ContactErrors.NameRequired;
        }

        tckn = Clean(tckn);
        if (tckn is not null && !TurkishIdentityNumber.IsValidTckn(tckn))
        {
            return ContactErrors.InvalidTckn;
        }

        var contact = new Contact
        {
            Type = ContactType.Person,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            NationalId = tckn,
        };
        contact.DisplayName = $"{contact.FirstName} {contact.LastName}";

        return contact.SetChannels(email, phone);
    }

    public static Result<Contact> CreateOrganization(string name, string? email, string? phone, string? vkn)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ContactErrors.NameRequired;
        }

        vkn = Clean(vkn);
        if (vkn is not null && !TurkishIdentityNumber.IsValidVknFormat(vkn))
        {
            return ContactErrors.InvalidVkn;
        }

        var contact = new Contact
        {
            Type = ContactType.Organization,
            OrganizationName = name.Trim(),
            DisplayName = name.Trim(),
            NationalId = vkn,
        };

        return contact.SetChannels(email, phone);
    }

    public string? MaskedNationalId => NationalId is null ? null : TurkishIdentityNumber.Mask(NationalId);

    public ContactSummary ToSummary() => new(Id, Type, DisplayName, Email, Phone);

    /// <summary>Aramada kullanılan normalleştirme: "İSTANBUL" ve "istanbul" aynı sonucu verir.</summary>
    public static string Normalize(string value) => value.Trim().ToLower(Turkish);

    private Result<Contact> SetChannels(string? email, string? phone)
    {
        email = Clean(email);
        if (email is not null && (!email.Contains('@') || email.Length > 254))
        {
            return ContactErrors.InvalidEmail;
        }

        Email = email?.ToLowerInvariant();
        Phone = Clean(phone);
        SearchText = Normalize($"{DisplayName} {Email}");

        return this;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ContactErrors
{
    public static readonly Error NameRequired = new("contacts.name_required", "Ad alanı zorunludur.");

    public static readonly Error InvalidTckn = new("contacts.invalid_tckn", "T.C. kimlik numarası geçerli değil.");

    public static readonly Error InvalidVkn = new("contacts.invalid_vkn", "Vergi kimlik numarası 10 haneli olmalıdır.");

    public static readonly Error InvalidEmail = new("contacts.invalid_email", "E-posta adresi geçerli değil.");
}
