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

public class ClassNamesTests
{
    [Theory]
    [InlineData("w-full px-3", "w-36", "w-36 px-3")]
    [InlineData("h-9 text-base md:text-sm", "h-7 text-xs", "h-7 text-xs md:text-sm")]
    [InlineData("text-sm text-muted-foreground", "text-red-500", "text-sm text-red-500")]
    [InlineData("p-2 hover:bg-accent", "bg-primary", "p-2 hover:bg-accent bg-primary")]
    [InlineData("rounded-md", "rounded-t-lg", "rounded-md rounded-t-lg")]
    [InlineData("font-medium text-sm", "font-mono text-left", "font-medium text-sm font-mono text-left")]
    [InlineData("bg-transparent bg-[length:16px] bg-no-repeat", "bg-muted", "bg-muted bg-[length:16px] bg-no-repeat")]
    [InlineData("p-2 whitespace-nowrap", "whitespace-normal", "p-2 whitespace-normal")]
    public void Later_conflicting_utility_wins(string baseClasses, string extra, string expected) =>
        ClassNames.Cn(baseClasses, extra).ShouldBe(expected);
}
