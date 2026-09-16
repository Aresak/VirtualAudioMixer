using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Vam.Protocol;
using Vam.Protocol.V1;
using Vam.Ui.Abstractions;
using Vam.Ui.Components;
using Vam.Ui.Localization;
using Vam.Ui.Services;
using Vam.Ui.State;
using Vam.Ui.Views;

namespace Vam.Ui.Components;

/// <summary>The code behind <c>ChainEditor.razor</c>.</summary>
public partial class ChainEditor
{
    string adding = string.Empty;
    int dragging = -1;

    /// <summary>Whose chain this is.</summary>
    [Parameter]
    [EditorRequired]
    public required ChainTarget Target { get; set; }

    /// <summary>The links, in the order they run.</summary>
    [Parameter]
    public IReadOnlyList<ModifierState> Chain { get; set; } = [];

    /// <summary>
    /// Whether the last link is the limiter the engine added itself. D6.
    /// </summary>
    /// <remarks>
    /// Shown as an anchored link rather than hidden. An operator who cannot see the limiter cannot
    /// tell why the stream stopped getting louder, and would go looking for the fault somewhere else.
    /// </remarks>
    [Parameter]
    public bool HasMandatoryLimiter { get; set; }

    bool IsLocked(int index) => HasMandatoryLimiter && index == Chain.Count - 1;

    static string Format(ParameterState parameter) =>
        parameter.Value.ToString("0.##", CultureInfo.InvariantCulture)
        + (parameter.Unit.Length > 0 ? " " + parameter.Unit : string.Empty);

    static string Step(ParameterState parameter)
    {
        double span = parameter.Maximum - parameter.Minimum;

        // A thousand steps across whatever the range happens to be. A fixed step of 0.1 is far too
        // coarse for a ratio and far too fine for a frequency that runs to twenty thousand.
        return (span <= 0 ? 0.01 : span / 1000.0).ToString("0.#####", CultureInfo.InvariantCulture);
    }

    string HelpFor(ModifierState link) => L["help.mod." + link.ModifierId];

    /// <summary>
    /// How to set one parameter well.
    /// </summary>
    /// <remarks>
    /// Looked up by parameter first and by its bare name second, so a third-party modifier that
    /// happens to call something "threshold" inherits an explanation that is true of thresholds
    /// rather than getting a key on screen.
    /// </remarks>
    string HelpForParameter(ModifierState link, ParameterState parameter)
    {
        string specific = L["help.par." + link.ModifierId + "." + parameter.Id];

        if (!specific.StartsWith("help.par.", StringComparison.Ordinal))
        {
            return specific;
        }

        string generic = L["help.par." + Bare(parameter.Id)];

        return generic.StartsWith("help.par.", StringComparison.Ordinal) ? string.Empty : generic;
    }

    // "band2.frequency" is a frequency. The band it belongs to does not change the advice.
    static string Bare(string id)
    {
        int dot = id.LastIndexOf('.');

        return dot >= 0 && dot < id.Length - 1 ? id[(dot + 1)..] : id;
    }

    async Task ToggleBypassAsync(int index, ModifierState link) =>
        await Session.ApplyAsync(new Command
        {
            SetModifierBypass = new SetModifierBypass
            {
                Target = Target,
                LinkIndex = index,
                Bypassed = !link.IsBypassed
            }
        });

    async Task SetParameterAsync(int index, ParameterState parameter, ChangeEventArgs arguments)
    {
        if (!double.TryParse(arguments.Value?.ToString(), CultureInfo.InvariantCulture, out double value))
        {
            return;
        }

        await Session.ApplyAsync(new Command
        {
            SetModifierParameter = new SetModifierParameter
            {
                Target = Target,
                LinkIndex = index,
                ParameterId = parameter.Id,
                Value = value
            }
        });
    }

    async Task RemoveAsync(int index) =>
        await Session.ApplyAsync(new Command
        {
            RemoveModifier = new RemoveModifier { Target = Target, LinkIndex = index }
        });

    async Task MoveAsync(int to)
    {
        if (dragging < 0 || dragging == to || IsLocked(to))
        {
            return;
        }

        int from = dragging;

        dragging = -1;

        await Session.ApplyAsync(new Command
        {
            MoveModifier = new MoveModifier { Target = Target, FromIndex = from, ToIndex = to }
        });
    }

    async Task AddAsync()
    {
        if (adding.Length == 0)
        {
            return;
        }

        // Before the mandatory limiter, never after it. A modifier downstream of the brick wall is a
        // modifier that can undo it, which is the one thing the mandatory limiter exists to prevent.
        int at = HasMandatoryLimiter ? Math.Max(Chain.Count - 1, 0) : Chain.Count;

        await Session.ApplyAsync(new Command
        {
            AddModifier = new AddModifier { Target = Target, ModifierId = adding, AtIndex = at }
        });

        adding = string.Empty;
    }
}
