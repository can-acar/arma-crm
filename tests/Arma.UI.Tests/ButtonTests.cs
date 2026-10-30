using Bunit;

namespace Arma.UI.Tests;

public class ButtonTests : BunitContext
{
    [Fact]
    public void Renders_button_with_variant_and_size_classes()
    {
        var cut = Render<Button>(p => p
            .Add(b => b.Variant, ButtonVariant.Destructive)
            .Add(b => b.Size, ButtonSize.Sm)
            .AddChildContent("Sil"));

        var button = cut.Find("button");
        button.TextContent.ShouldBe("Sil");
        button.GetAttribute("type").ShouldBe("button");
        button.ClassList.ShouldContain("bg-destructive");
        button.ClassList.ShouldContain("h-8");
    }

    [Fact]
    public void Renders_link_when_href_is_set()
    {
        var cut = Render<Button>(p => p.Add(b => b.Href, "contacts").AddChildContent("Kişiler"));

        cut.Find("a").GetAttribute("href").ShouldBe("contacts");
        cut.FindAll("button").ShouldBeEmpty();
    }

    [Fact]
    public void Appends_caller_class_last()
    {
        var cut = Render<Button>(p => p.Add(b => b.Class, "w-full"));

        cut.Find("button").GetAttribute("class")!.ShouldEndWith("w-full");
    }

    [Fact]
    public void Click_invokes_callback()
    {
        var clicks = 0;
        var cut = Render<Button>(p => p.Add(b => b.OnClick, () => clicks++));

        cut.Find("button").Click();

        clicks.ShouldBe(1);
    }
}
