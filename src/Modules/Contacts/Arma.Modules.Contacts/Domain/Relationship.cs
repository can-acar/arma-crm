using Arma.SharedKernel.Domain;

namespace Arma.Modules.Contacts.Domain;

public enum RelationshipType
{
    EmployeeOf = 1,
    SpouseOf = 2,
    ParentOf = 3,
    SiblingOf = 4,
    HouseholdMemberOf = 5,
    BoardMemberOf = 6,
    VolunteerAt = 7,
    Other = 99,
}

/// <summary>İki kayıt arasındaki yönlü ilişki: "Ayşe, X Derneği'nin çalışanıdır".</summary>
public sealed class Relationship : Entity
{
    private Relationship()
    {
    }

    public Guid FromContactId { get; private set; }

    public Guid ToContactId { get; private set; }

    public RelationshipType Type { get; private set; }

    public string? Description { get; private set; }

    public DateOnly? StartDate { get; private set; }

    public DateOnly? EndDate { get; private set; }

    public bool IsActive => EndDate is null;

    public static Result<Relationship> Create(Guid from, Guid to, RelationshipType type, string? description, DateOnly? startDate)
    {
        if (from == to)
        {
            return new Error("relationships.self", "Bir kayıt kendisiyle ilişkilendirilemez.");
        }

        return new Relationship
        {
            FromContactId = from,
            ToContactId = to,
            Type = type,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            StartDate = startDate,
        };
    }

    public void End(DateOnly date) => EndDate = date;

    /// <summary>Birleştirmede mükerrer kaydın ilişkileri asıl kayda taşınır.</summary>
    public void Repoint(Guid from, Guid to)
    {
        if (FromContactId == from)
        {
            FromContactId = to;
        }

        if (ToContactId == from)
        {
            ToContactId = to;
        }
    }

    /// <summary>İlişkinin, kaydın kendi açısından okunuşu.</summary>
    public static string Label(RelationshipType type, bool fromPerspective) => (type, fromPerspective) switch
    {
        (RelationshipType.EmployeeOf, true) => "Çalıştığı yer",
        (RelationshipType.EmployeeOf, false) => "Çalışanı",
        (RelationshipType.SpouseOf, _) => "Eşi",
        (RelationshipType.ParentOf, true) => "Çocuğu",
        (RelationshipType.ParentOf, false) => "Ebeveyni",
        (RelationshipType.SiblingOf, _) => "Kardeşi",
        (RelationshipType.HouseholdMemberOf, true) => "Hanesi",
        (RelationshipType.HouseholdMemberOf, false) => "Hane üyesi",
        (RelationshipType.BoardMemberOf, true) => "Yönetim kurulu üyesi olduğu yer",
        (RelationshipType.BoardMemberOf, false) => "Yönetim kurulu üyesi",
        (RelationshipType.VolunteerAt, true) => "Gönüllüsü olduğu yer",
        (RelationshipType.VolunteerAt, false) => "Gönüllüsü",
        _ => "İlişkili",
    };

    public static string TypeLabel(RelationshipType type) => type switch
    {
        RelationshipType.EmployeeOf => "… şurada çalışır",
        RelationshipType.SpouseOf => "… şunun eşidir",
        RelationshipType.ParentOf => "… şunun ebeveynidir",
        RelationshipType.SiblingOf => "… şunun kardeşidir",
        RelationshipType.HouseholdMemberOf => "… şu hanenin üyesidir",
        RelationshipType.BoardMemberOf => "… şunun yönetim kurulundadır",
        RelationshipType.VolunteerAt => "… şurada gönüllüdür",
        _ => "… şununla ilişkilidir",
    };
}
