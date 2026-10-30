using Microsoft.AspNetCore.Components;

namespace Arma.UI;

/// <summary>Tüm Arma.UI bileşenlerinin ortak parametreleri.</summary>
public abstract class ArmaComponentBase : ComponentBase
{
    /// <summary>Temel sınıflara eklenecek Tailwind sınıfları.</summary>
    [Parameter]
    public string? Class { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }
}
