using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Auditing;
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
    [SensitiveData]
    public string? NationalId { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    public string? Website { get; private set; }

    /// <summary>Türkçe kurallarla küçük harfe çevrilmiş ad, e-posta ve telefon; arama bu alan üzerinden yapılır.</summary>
    [NotAudited]
    public string SearchText { get; private set; } = string.Empty;

    /// <summary>Mükerrer kayıt birleştirildiyse asıl kaydın kimliği. Diğer modüller eski kimliği buradan çözer.</summary>
    public Guid? MergedIntoId { get; private set; }

    public List<Address> Addresses { get; private set; } = [];

    public List<Tag> Tags { get; private set; } = [];

    public List<Group> Groups { get; private set; } = [];

    public Address? PrimaryAddress => Addresses.FirstOrDefault(a => a.IsPrimary) ?? Addresses.FirstOrDefault();

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

    public static Result<Contact> CreateHousehold(string name, string? email, string? phone)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return ContactErrors.NameRequired;
        }

        var contact = new Contact { Type = ContactType.Household, OrganizationName = name.Trim(), DisplayName = name.Trim() };
        return contact.SetChannels(email, phone);
    }

    public Result<Contact> UpdatePerson(string firstName, string lastName, string? email, string? phone, string? tckn, DateOnly? birthDate)
    {
        if (Type != ContactType.Person)
        {
            return ContactErrors.WrongType;
        }

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName))
        {
            return ContactErrors.NameRequired;
        }

        tckn = Clean(tckn);
        if (tckn is not null && !TurkishIdentityNumber.IsValidTckn(tckn))
        {
            return ContactErrors.InvalidTckn;
        }

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        DisplayName = $"{FirstName} {LastName}";
        NationalId = tckn;
        BirthDate = birthDate;
        return SetChannels(email, phone);
    }

    public Result<Contact> UpdateOrganization(string name, string? email, string? phone, string? vkn, string? website)
    {
        if (Type == ContactType.Person)
        {
            return ContactErrors.WrongType;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return ContactErrors.NameRequired;
        }

        vkn = Clean(vkn);
        if (Type == ContactType.Organization && vkn is not null && !TurkishIdentityNumber.IsValidVknFormat(vkn))
        {
            return ContactErrors.InvalidVkn;
        }

        OrganizationName = name.Trim();
        DisplayName = OrganizationName;
        NationalId = Type == ContactType.Organization ? vkn : null;
        Website = Clean(website);
        return SetChannels(email, phone);
    }

    /// <summary>Adres listesini değiştirir; tek birincil adres kalır.</summary>
    public void SetAddresses(IEnumerable<Address> addresses)
    {
        var list = addresses.Where(a => !a.IsEmpty).ToList();
        if (list.Count > 0 && list.Count(a => a.IsPrimary) != 1)
        {
            list = list.Select((a, i) => a with { IsPrimary = i == 0 }).ToList();
        }

        Addresses = list;
    }

    /// <summary>
    /// Bu kaydın eksik alanlarını <paramref name="duplicate"/> kaydından doldurur, etiket, grup ve adreslerini alır;
    /// mükerrer kaydı bu kayda yönlendirip siler.
    /// </summary>
    public Result<Contact> Absorb(Contact duplicate)
    {
        if (duplicate.Id == Id)
        {
            return ContactErrors.MergeSelf;
        }

        if (duplicate.Type != Type)
        {
            return ContactErrors.MergeTypeMismatch;
        }

        Email ??= duplicate.Email;
        Phone ??= duplicate.Phone;
        NationalId ??= duplicate.NationalId;
        BirthDate ??= duplicate.BirthDate;
        Website ??= duplicate.Website;

        foreach (var tag in duplicate.Tags.Where(t => Tags.All(x => x.Id != t.Id)))
        {
            Tags.Add(tag);
        }

        foreach (var group in duplicate.Groups.Where(g => Groups.All(x => x.Id != g.Id)))
        {
            Groups.Add(group);
        }

        var known = Addresses.Select(a => a with { IsPrimary = false }).ToHashSet();
        SetAddresses(Addresses.Concat(duplicate.Addresses.Select(a => a with { IsPrimary = false }).Where(known.Add)));
        SearchText = BuildSearchText();

        duplicate.MergedIntoId = Id;
        duplicate.IsDeleted = true;
        return this;
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
        SearchText = BuildSearchText();

        return this;
    }

    private string BuildSearchText() => Normalize($"{DisplayName} {Email} {PhoneDigits(Phone)}");

    /// <summary>Telefonun yalnızca rakamları; "0532 111 22 33" ile "+90 532 1112233" aynı aranır.</summary>
    public static string PhoneDigits(string? phone)
    {
        if (string.IsNullOrEmpty(phone))
        {
            return string.Empty;
        }

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("90") && digits.Length == 12)
        {
            digits = digits[2..];
        }

        return digits.TrimStart('0');
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ContactErrors
{
    public static readonly Error NameRequired = new("contacts.name_required", "Ad alanı zorunludur.");

    public static readonly Error InvalidTckn = new("contacts.invalid_tckn", "T.C. kimlik numarası geçerli değil.");

    public static readonly Error InvalidVkn = new("contacts.invalid_vkn", "Vergi kimlik numarası 10 haneli olmalıdır.");

    public static readonly Error InvalidEmail = new("contacts.invalid_email", "E-posta adresi geçerli değil.");

    public static readonly Error WrongType = new("contacts.wrong_type", "Bu işlem bu kayıt türü için geçerli değil.");

    public static readonly Error NotFound = new("contacts.not_found", "Kayıt bulunamadı.");

    public static readonly Error DuplicateNationalId = new("contacts.duplicate_national_id", "Bu kimlik numarasıyla kayıtlı başka bir kişi var.");

    public static readonly Error MergeSelf = new("contacts.merge_self", "Bir kayıt kendisiyle birleştirilemez.");

    public static readonly Error MergeTypeMismatch = new("contacts.merge_type", "Yalnızca aynı türdeki kayıtlar birleştirilebilir.");
}
