using Microsoft.AspNetCore.Components;
using Vam.Ui.Abstractions;
using Vam.Ui.Localization;
using Vam.Ui.State;

namespace Vam.Ui.Components;

/// <summary>
/// A component that redraws when the engine, the shell or the language changes.
/// </summary>
/// <remarks>
/// <para>
/// Three sources, one place to subscribe and one place to unsubscribe. Doing it per component is how
/// a console ends up with a leak that only shows after an hour, which for this software means during
/// a meeting.
/// </para>
/// <para>
/// <b>Meter frames are not one of the three.</b> They never reach a component; they go to a canvas.
/// See <c>wwwroot/js/vam-meters.js</c>.
/// </para>
/// </remarks>
public abstract class VamComponent : ComponentBase, IDisposable
{
    /// <summary>The engine this console is looking at.</summary>
    [Inject]
    public required IVamSession Session { get; set; }

    /// <summary>What this console is looking at.</summary>
    [Inject]
    public required ShellState Shell { get; set; }

    /// <summary>The console's words. U7.</summary>
    [Inject]
    public required VamLocalizer L { get; set; }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Session.Changed += OnChanged;
        Shell.Changed += OnChanged;
        L.Changed += OnChanged;
    }

    /// <summary>Stops listening.</summary>
    public void Dispose()
    {
        Session.Changed -= OnChanged;
        Shell.Changed -= OnChanged;
        L.Changed -= OnChanged;

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Lets a derived component let go of its own things.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }

    void OnChanged() => _ = InvokeAsync(StateHasChanged);
}
