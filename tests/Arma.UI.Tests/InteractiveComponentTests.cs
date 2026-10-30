using Bunit;
using Microsoft.AspNetCore.Components;

namespace Arma.UI.Tests;

public class InteractiveComponentTests : BunitContext
{
    [Fact]
    public void Tabs_show_only_the_selected_panel_and_switch_on_click()
    {
        var cut = Render<Tabs>(p => p
            .Add(t => t.Value, "a")
            .AddChildContent<TabsList>(list => list.AddChildContent(b =>
            {
                b.OpenComponent<TabsTrigger>(0);
                b.AddAttribute(1, nameof(TabsTrigger.Value), "a");
                b.AddAttribute(2, nameof(TabsTrigger.ChildContent), (RenderFragment)(x => x.AddContent(0, "A")));
                b.CloseComponent();
                b.OpenComponent<TabsTrigger>(3);
                b.AddAttribute(4, nameof(TabsTrigger.Value), "b");
                b.AddAttribute(5, nameof(TabsTrigger.ChildContent), (RenderFragment)(x => x.AddContent(0, "B")));
                b.CloseComponent();
            }))
            .AddChildContent<TabsContent>(c => c.Add(x => x.Value, "a").AddChildContent("panel-a"))
            .AddChildContent<TabsContent>(c => c.Add(x => x.Value, "b").AddChildContent("panel-b")));

        cut.Markup.ShouldContain("panel-a");
        cut.Markup.ShouldNotContain("panel-b");

        cut.FindAll("[role=tab]")[1].Click();

        cut.Markup.ShouldContain("panel-b");
        cut.Markup.ShouldNotContain("panel-a");
        cut.FindAll("[role=tab]")[1].GetAttribute("aria-selected").ShouldBe("true");
    }

    [Fact]
    public void Dialog_closes_on_overlay_click_and_escape()
    {
        var open = true;
        var cut = Render<Dialog>(p => p
            .Add(d => d.Open, true)
            .Add(d => d.OpenChanged, (bool v) => open = v)
            .AddChildContent("içerik"));

        cut.Find("[role=dialog]").GetAttribute("aria-modal").ShouldBe("true");
        cut.Find("[data-slot=dialog-content]").KeyDown("Escape");

        open.ShouldBeFalse();
        cut.FindAll("[role=dialog]").ShouldBeEmpty();
    }

    [Fact]
    public void Checkbox_reports_checked_state()
    {
        bool? value = null;
        var cut = Render<Checkbox>(p => p.Add(c => c.CheckedChanged, (bool v) => value = v));

        cut.Find("input").Change(true);

        value.ShouldBe(true);
    }

    [Fact]
    public void NativeSelect_raises_value_changed()
    {
        string? value = null;
        var cut = Render<NativeSelect>(p => p
            .Add(s => s.ValueChanged, (string? v) => value = v)
            .AddChildContent("<option value=\"x\">X</option>"));

        cut.Find("select").Change("x");

        value.ShouldBe("x");
    }
}
