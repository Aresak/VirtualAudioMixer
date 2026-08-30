namespace Vam.Ui.State;

/// <summary>
/// What this console is looking at, as opposed to what the engine is doing.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately separate from <see cref="Abstractions.IVamSession"/>. Which strip is selected and
/// whether the strips are drawn narrow are decisions belonging to the person sitting in front of
/// this screen, and two consoles watching the same meeting must be able to disagree about them. A
/// selection sent to the engine would move under an operator whenever somebody else clicked.
/// </para>
/// <para>
/// Nothing here is on a hot path.
/// </para>
/// </remarks>
public sealed class ShellState
{
    ViewId view = ViewId.Mixer;
    OverlayId overlay = OverlayId.None;
    int selectedChannel = -1;
    int selectedBus = -1;
    bool isCompact;
    bool isMonitorBarOpen = true;
    bool isReorderArmed;

    /// <summary>Raised when something the shell draws has changed.</summary>
    public event Action? Changed;

    /// <summary>Which view the rail is on.</summary>
    public ViewId View
    {
        get => view;
        set => Set(ref view, value);
    }

    /// <summary>Which overlay is over the mixer.</summary>
    public OverlayId Overlay
    {
        get => overlay;
        set => Set(ref overlay, value);
    }

    /// <summary>The strip the channel overlay is about, or -1.</summary>
    public int SelectedChannel
    {
        get => selectedChannel;
        set => Set(ref selectedChannel, value);
    }

    /// <summary>The bus the bus overlay is about, or -1.</summary>
    public int SelectedBus
    {
        get => selectedBus;
        set => Set(ref selectedBus, value);
    }

    /// <summary>U4. Narrow strips: meters, names and mutes, and nothing else.</summary>
    /// <remarks>
    /// Sixteen full strips do not fit on a laptop, and the answer is not a horizontal scrollbar an
    /// operator has to hunt along while somebody is talking.
    /// </remarks>
    public bool IsCompact
    {
        get => isCompact;
        set => Set(ref isCompact, value);
    }

    /// <summary>Whether the D5 monitor bar is showing.</summary>
    public bool IsMonitorBarOpen
    {
        get => isMonitorBarOpen;
        set => Set(ref isMonitorBarOpen, value);
    }

    /// <summary>
    /// U13. Strips only move when reordering has been switched on.
    /// </summary>
    /// <remarks>
    /// A fader is a drag and a strip is a drag, and on a touchscreen they are the same gesture. An
    /// operator reaching for a level must not be able to rearrange the console by missing by six
    /// pixels, so dragging a strip is something you ask for first.
    /// </remarks>
    public bool IsReorderArmed
    {
        get => isReorderArmed;
        set => Set(ref isReorderArmed, value);
    }

    /// <summary>Opens the channel overlay on one strip.</summary>
    /// <param name="index">Which strip.</param>
    public void OpenChannel(int index)
    {
        selectedChannel = index;
        overlay = OverlayId.Channel;
        Changed?.Invoke();
    }

    /// <summary>Opens the bus overlay on one bus.</summary>
    /// <param name="index">Which bus.</param>
    public void OpenBus(int index)
    {
        selectedBus = index;
        overlay = OverlayId.Bus;
        Changed?.Invoke();
    }

    /// <summary>Closes whatever overlay is open.</summary>
    public void CloseOverlay() => Overlay = OverlayId.None;

    void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Changed?.Invoke();
    }
}
