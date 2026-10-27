using System.Globalization;
using Microsoft.AspNetCore.Components;
using Vam.Ui.State;

namespace Vam.Ui.Components;

/// <summary>The code behind <c>Slider.razor</c>.</summary>
public partial class Slider
{
    readonly SliderDrag drag = new();

    /// <summary>The engine's value.</summary>
    [Parameter]
    public double Value { get; set; }

    /// <summary>The bottom of the travel.</summary>
    [Parameter]
    public double Minimum { get; set; }

    /// <summary>The top of the travel.</summary>
    [Parameter]
    public double Maximum { get; set; } = 1;

    /// <summary>The smallest move.</summary>
    [Parameter]
    public double Step { get; set; } = 0.01;

    /// <summary>What it sets, for a screen reader.</summary>
    [Parameter]
    public string Label { get; set; } = string.Empty;

    /// <summary>Sends a value the slider was moved to.</summary>
    [Parameter]
    public EventCallback<double> Changed { get; set; }

    // Yielding renders twice: once at the value let go at, then at the engine's. See SliderDrag.Release.
    async Task ReleaseAsync()
    {
        drag.Release();
        await Task.Yield();
    }

    Task OnInputAsync(ChangeEventArgs arguments) =>
        double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double value)
            ? drag.MoveAsync(value, Changed.InvokeAsync)
            : Task.CompletedTask;

    static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}
