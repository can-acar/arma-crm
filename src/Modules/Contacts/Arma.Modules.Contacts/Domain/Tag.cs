using Arma.SharedKernel.Domain;

namespace Arma.Modules.Contacts.Domain;

/// <summary>Kişilere serbestçe eklenen etiket ("Gönüllü", "Basın" gibi).</summary>
public sealed class Tag : Entity
{
    private Tag()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Arayüzdeki renk anahtarı: gray, red, amber, green, blue, purple.</summary>
    public string Color { get; private set; } = "gray";

    public List<Contact> Contacts { get; private set; } = [];

    public static readonly string[] Colors = ["gray", "red", "amber", "green", "blue", "purple"];

    public static Result<Tag> Create(string name, string? color)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 60)
        {
            return new Error("tags.name", "Etiket adı 1-60 karakter olmalı.");
        }

        return new Tag { Name = name.Trim(), Color = Colors.Contains(color) ? color! : "gray" };
    }

    public void Rename(string name, string? color)
    {
        Name = name.Trim();
        Color = Colors.Contains(color) ? color! : Color;
    }
}

/// <summary>Elle üye eklenen statik grup ("2026 Yönetim Kurulu", "Bülten aboneleri" gibi).</summary>
public sealed class Group : Entity
{
    private Group()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public List<Contact> Members { get; private set; } = [];

    public static Result<Group> Create(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            return new Error("groups.name", "Grup adı 1-120 karakter olmalı.");
        }

        return new Group { Name = name.Trim(), Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim() };
    }

    public void Update(string name, string? description)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}
