using Bunit;

namespace Arma.UI.Tests;

public class ComponentTests : BunitContext
{
    [Fact]
    public void Cn_skips_empty_values_and_duplicates() =>
        ClassNames.Cn("px-2 py-1", null, " ", "px-2 font-medium").ShouldBe("px-2 py-1 font-medium");

    [Fact]
    public void Input_raises_value_changed()
    {
        string? value = null;
        var cut = Render<Input>(p => p.Add(i => i.ValueChanged, (string? v) => value = v));

        cut.Find("input").Input("Ankara");

        value.ShouldBe("Ankara");
    }

    [Fact]
    public void Input_marks_invalid_state_for_assistive_tech()
    {
        var cut = Render<Input>(p => p.Add(i => i.Invalid, true));

        cut.Find("input").GetAttribute("aria-invalid").ShouldBe("true");
    }

    [Fact]
    public void Destructive_alert_has_alert_role()
    {
        var cut = Render<Alert>(p => p.Add(a => a.Variant, AlertVariant.Destructive).AddChildContent("Hata"));

        var alert = cut.Find("[data-slot=alert]");
        alert.GetAttribute("role").ShouldBe("alert");
        alert.ClassList.ShouldContain("text-destructive");
    }

    [Fact]
    public void Card_parts_carry_data_slots()
    {
        var cut = Render<Card>(p => p.AddChildContent<CardHeader>(h => h.AddChildContent<CardTitle>(t => t.AddChildContent("Başlık"))));

        cut.Find("[data-slot=card] [data-slot=card-header] [data-slot=card-title]").TextContent.ShouldBe("Başlık");
    }
}
