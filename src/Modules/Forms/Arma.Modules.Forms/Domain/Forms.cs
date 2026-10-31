using System.Text.Json;
using System.Text.RegularExpressions;
using Arma.SharedKernel.Domain;

namespace Arma.Modules.Forms.Domain;

public enum FieldType
{
    Text = 1,
    Email = 2,
    Phone = 3,
    TextArea = 4,
    Select = 5,
    Checkbox = 6,
    Date = 7,
    Number = 8,
}

/// <summary>Kişi kaydına aktarılan alanlar. Ad, soyad ve e-posta eşlenirse yanıt kişiye bağlanır.</summary>
public static class FieldMap
{
    public const string FirstName = "first_name";
    public const string LastName = "last_name";
    public const string Email = "email";
    public const string Phone = "phone";

    public static readonly string[] All = [FirstName, LastName, Email, Phone];
}

/// <param name="Key">Formdaki alan adı; yanıtlar bu anahtarla saklanır.</param>
/// <param name="MapsTo"><see cref="FieldMap"/> değerlerinden biri veya boş.</param>
public sealed record FormField(string Key, string Label, FieldType Type, bool Required, IReadOnlyList<string> Options, string? MapsTo, string? Help);

/// <summary>Web sitesine gömülebilen form. Alan tanımı JSON olarak saklanır.</summary>
public sealed partial class WebForm : Entity
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WebForm()
    {
    }

    public string Name { get; private set; } = "";

    /// <summary>Kamuya açık adres: /f/{Slug}.</summary>
    public string Slug { get; private set; } = "";

    public string Title { get; private set; } = "";

    public string? Intro { get; private set; }

    public string SuccessMessage { get; private set; } = "";

    public bool IsActive { get; private set; }

    public string FieldsJson { get; private set; } = "[]";

    public string? TagName { get; private set; }

    public Guid? GroupId { get; private set; }

    public bool AskEmailConsent { get; private set; }

    public bool AskSmsConsent { get; private set; }

    /// <summary>İşaretlenmesi zorunlu açık rıza metni (KVKK modülündeki adresi).</summary>
    public string? ConsentNoticeSlug { get; private set; }

    /// <summary>Formun altında bağlantısı gösterilen aydınlatma metni.</summary>
    public string? InfoNoticeSlug { get; private set; }

    public IReadOnlyList<FormField> Fields => JsonSerializer.Deserialize<List<FormField>>(FieldsJson, Json) ?? [];

    public bool CreatesContact => Fields.Count(f => f.MapsTo is FieldMap.FirstName or FieldMap.LastName or FieldMap.Email) == 3;

    public static Result<WebForm> Create(FormSettings settings, IReadOnlyList<FormField> fields)
    {
        var form = new WebForm();
        var updated = form.Update(settings, fields);
        return updated.IsSuccess ? form : updated.Error!;
    }

    public Result<bool> Update(FormSettings settings, IReadOnlyList<FormField> fields)
    {
        var slug = (settings.Slug ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Title))
        {
            return new Error("forms.required", "Form adı ve başlığı gerekli.");
        }

        if (!SlugPattern().IsMatch(slug))
        {
            return new Error("forms.slug", "Adres yalnızca küçük harf, rakam ve tire içerebilir (örnek: gonullu-basvuru).");
        }

        if (fields.Count == 0)
        {
            return new Error("forms.no_fields", "Forma en az bir alan ekleyin.");
        }

        if (fields.Any(f => string.IsNullOrWhiteSpace(f.Label)))
        {
            return new Error("forms.field_label", "Her alanın bir etiketi olmalı.");
        }

        if (fields.Any(f => f.Type == FieldType.Select && f.Options.Count == 0))
        {
            return new Error("forms.select_options", "Seçim alanlarına en az bir seçenek yazın.");
        }

        var mapped = fields.Where(f => f.MapsTo is not null).ToList();
        if (mapped.Any(f => !FieldMap.All.Contains(f.MapsTo)) || mapped.GroupBy(f => f.MapsTo).Any(g => g.Count() > 1))
        {
            return new Error("forms.mapping", "Her kişi alanı (ad, soyad, e-posta, telefon) en fazla bir alana eşlenebilir.");
        }

        var names = mapped.Count(f => f.MapsTo is FieldMap.FirstName or FieldMap.LastName or FieldMap.Email);
        if (names is > 0 and < 3)
        {
            return new Error("forms.mapping_incomplete", "Yanıtın kişiye bağlanması için ad, soyad ve e-posta alanlarının üçü de eşlenmeli.");
        }

        if (names < 3 && (settings.AskEmailConsent || settings.AskSmsConsent || settings.ConsentNoticeSlug is not null || settings.TagName is not null || settings.GroupId is not null))
        {
            return new Error("forms.needs_contact", "İzin, rıza, etiket ve grup işlemleri için ad, soyad ve e-posta alanlarını kişi kaydına eşleyin.");
        }

        if (settings.AskSmsConsent && mapped.All(f => f.MapsTo != FieldMap.Phone))
        {
            return new Error("forms.sms_needs_phone", "SMS izni sormak için bir alanı telefona eşleyin.");
        }

        // Yeni alanlara, eski yanıtlarla karışmasın diye daha önce kullanılmamış bir anahtar verilir.
        var used = fields.Select(f => f.Key).Where(k => !string.IsNullOrWhiteSpace(k)).ToHashSet();
        var next = 1;
        string NewKey()
        {
            while (!used.Add($"f{next}"))
            {
                next++;
            }

            return $"f{next}";
        }

        var keyed = fields.Select(f => f with
        {
            Key = string.IsNullOrWhiteSpace(f.Key) ? NewKey() : f.Key,
            Label = f.Label.Trim(),
            Options = [.. f.Options.Select(o => o.Trim()).Where(o => o.Length > 0)],
            Required = f.Required || f.MapsTo is FieldMap.FirstName or FieldMap.LastName or FieldMap.Email,
        }).ToList();
        if (keyed.Select(f => f.Key).Distinct().Count() != keyed.Count)
        {
            return new Error("forms.duplicate_key", "Alan anahtarları benzersiz olmalı.");
        }

        Name = settings.Name.Trim();
        Slug = slug;
        Title = settings.Title.Trim();
        Intro = string.IsNullOrWhiteSpace(settings.Intro) ? null : settings.Intro.Trim();
        SuccessMessage = string.IsNullOrWhiteSpace(settings.SuccessMessage) ? "Teşekkür ederiz, yanıtınız alındı." : settings.SuccessMessage.Trim();
        IsActive = settings.IsActive;
        TagName = string.IsNullOrWhiteSpace(settings.TagName) ? null : settings.TagName.Trim();
        GroupId = settings.GroupId;
        AskEmailConsent = settings.AskEmailConsent;
        AskSmsConsent = settings.AskSmsConsent;
        ConsentNoticeSlug = string.IsNullOrWhiteSpace(settings.ConsentNoticeSlug) ? null : settings.ConsentNoticeSlug;
        InfoNoticeSlug = string.IsNullOrWhiteSpace(settings.InfoNoticeSlug) ? null : settings.InfoNoticeSlug;
        FieldsJson = JsonSerializer.Serialize(keyed, Json);
        return true;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}

public sealed record FormSettings(
    string Name, string Slug, string Title, string? Intro, string? SuccessMessage, bool IsActive,
    string? TagName, Guid? GroupId, bool AskEmailConsent, bool AskSmsConsent, string? ConsentNoticeSlug, string? InfoNoticeSlug);

/// <summary>Bir form yanıtı. Değerler, gönderildiği andaki alan etiketleriyle saklanır; form sonradan değişse de okunur.</summary>
public sealed class FormSubmission : Entity
{
    private FormSubmission()
    {
    }

    public Guid FormId { get; private set; }

    public Guid? ContactId { get; private set; }

    /// <summary>[{"label": "...", "value": "..."}] biçiminde JSON.</summary>
    public string Values { get; private set; } = "[]";

    public string? IpAddress { get; private set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public static FormSubmission Create(Guid formId, Guid? contactId, string values, string? ipAddress, DateTimeOffset now) =>
        new() { FormId = formId, ContactId = contactId, Values = values, IpAddress = ipAddress, SubmittedAt = now };

    internal void ChangeContact(Guid contactId) => ContactId = contactId;
}
