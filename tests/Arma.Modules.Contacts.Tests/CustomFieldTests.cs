using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;

namespace Arma.Modules.Contacts.Tests;

public class CustomFieldTests
{
    private static CustomField Field(CustomFieldType type, params string[] options) =>
        CustomField.Create(new CustomFieldInput("Alan", type, options, null, null, 0, true)).Value!;

    [Theory]
    [InlineData("1.250,5", 1250.5)]
    [InlineData("1250.5", 1250.5)]
    [InlineData("1.250", 1250)]
    [InlineData("-12", -12)]
    public void Numbers_are_read_in_turkish_or_invariant_format(string text, double expected)
    {
        CustomField.TryParseNumber(text, out var number).ShouldBeTrue();
        number.ShouldBe((decimal)expected);
    }

    [Fact]
    public void Values_are_stored_in_an_invariant_format_and_displayed_in_turkish()
    {
        var number = Field(CustomFieldType.Number);
        number.Normalize("1.250,5").Value.ShouldBe("1250.5");
        number.Display("1250.5").ShouldBe("1.250,5");
        number.Normalize("çok").Error!.Code.ShouldBe("custom_fields.value");

        var date = Field(CustomFieldType.Date);
        date.Normalize("05.03.2026").Value.ShouldBe("2026-03-05");
        date.Display("2026-03-05").ShouldBe("05.03.2026");

        var flag = Field(CustomFieldType.YesNo);
        flag.Normalize("EVET").Value.ShouldBe("true");
        flag.Normalize("Hayır").Value.ShouldBe("false");
        flag.Display("false").ShouldBe("Hayır");

        var choice = Field(CustomFieldType.Choice, "Ankara", "İzmir");
        choice.Normalize("ankara").Value.ShouldBe("Ankara");
        choice.Normalize("İZMİR").Value.ShouldBe("İzmir");
        choice.Normalize("Bursa").IsSuccess.ShouldBeFalse();

        Field(CustomFieldType.Text).Normalize("  ").Value.ShouldBeNull();
    }

    [Fact]
    public void Choice_needs_options_and_label_is_required()
    {
        CustomField.Create(new CustomFieldInput("Şehir", CustomFieldType.Choice, [], null, null, 0, true)).IsSuccess.ShouldBeFalse();
        CustomField.Create(new CustomFieldInput(" ", CustomFieldType.Text, [], null, null, 0, true)).IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public void Merge_keeps_primary_values_and_fills_missing_ones()
    {
        var primary = Contact.CreatePerson("Ali", "Veli", null, null, null).Value!;
        var duplicate = Contact.CreatePerson("Ali", "Veli", null, null, null).Value!;
        primary.SetCustomFields(new Dictionary<string, string> { ["a"] = "1" });
        duplicate.SetCustomFields(new Dictionary<string, string> { ["a"] = "2", ["b"] = "3" });

        primary.Absorb(duplicate).IsSuccess.ShouldBeTrue();

        primary.CustomFields.ShouldBe(new Dictionary<string, string> { ["a"] = "1", ["b"] = "3" });
    }
}
