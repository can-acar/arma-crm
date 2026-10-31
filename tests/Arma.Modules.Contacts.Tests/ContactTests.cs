using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;

namespace Arma.Modules.Contacts.Tests;

public class ContactTests
{
    [Fact]
    public void Person_gets_display_name_and_normalized_channels()
    {
        var result = Contact.CreatePerson(" İsmail ", "Işık", " ISMAIL@Ornek.com ", " 0532 ", "10000000146");

        result.IsSuccess.ShouldBeTrue();
        var contact = result.Value!;
        contact.Type.ShouldBe(ContactType.Person);
        contact.DisplayName.ShouldBe("İsmail Işık");
        contact.Email.ShouldBe("ismail@ornek.com");
        contact.Phone.ShouldBe("0532");
        contact.MaskedNationalId.ShouldBe("100******46");
    }

    [Fact]
    public void Search_text_uses_turkish_casing()
    {
        var contact = Contact.CreatePerson("İsmail", "IŞIK", null, null, null).Value!;

        contact.SearchText.ShouldBe("ismail ışık");
        Contact.Normalize("IŞIK").ShouldBe("ışık");
    }

    [Theory]
    [InlineData("", "Işık", "contacts.name_required")]
    [InlineData("Ali", " ", "contacts.name_required")]
    public void Person_requires_names(string first, string last, string code) =>
        Contact.CreatePerson(first, last, null, null, null).Error!.Code.ShouldBe(code);

    [Fact]
    public void Person_rejects_invalid_tckn() =>
        Contact.CreatePerson("Ali", "Veli", null, null, "12345678901").Error.ShouldBe(ContactErrors.InvalidTckn);

    [Fact]
    public void Contact_rejects_invalid_email() =>
        Contact.CreatePerson("Ali", "Veli", "ali.ornek.com", null, null).Error.ShouldBe(ContactErrors.InvalidEmail);

    [Fact]
    public void Organization_is_created_with_vkn()
    {
        var contact = Contact.CreateOrganization("Çağdaş Eğitim Vakfı", null, null, "1234567890").Value!;

        contact.Type.ShouldBe(ContactType.Organization);
        contact.DisplayName.ShouldBe("Çağdaş Eğitim Vakfı");
        contact.MaskedNationalId.ShouldBe("123*****90");
    }

    [Fact]
    public void Organization_rejects_malformed_vkn() =>
        Contact.CreateOrganization("Vakıf", null, null, "123").Error.ShouldBe(ContactErrors.InvalidVkn);
}
