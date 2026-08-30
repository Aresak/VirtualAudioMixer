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

/// <summary>The code behind <c>EqualiserCurve.razor</c>.</summary>
public partial class EqualiserCurve
{
    // Drawn at exactly its own pixel size rather than stretched to the panel. That is what lets a
    // pointer's offset be read straight off as a user unit: a scaled viewBox would need the
    // element's rendered size, which means a round trip to JavaScript on every mouse move.
    const int Width = 360;
    const int Height = 140;

    // The band an operator is dragging, or -1. One at a time, because a curve is dragged with one
    // finger and two would be somebody's palm.
    int dragging = -1;

    // A drag produces a pointer move every few milliseconds, and each one here is two commands and a
    // console read back. Dropped rather than queued while one is in flight, exactly as the meter
    // frames are: a backlog of stale positions would make the handle lag behind the finger, which is
    // the one thing a direct-manipulation control must not do.
    bool isSending;

    static readonly double[] Decades = [50, 100, 500, 1000, 5000, 10000];

    /// <summary>The equaliser link whose curve this is.</summary>
    [Parameter]
    [EditorRequired]
    public required ModifierState Link { get; set; }

    /// <summary>Which chain it belongs to.</summary>
    [Parameter]
    [EditorRequired]
    public required ChainTarget Target { get; set; }

    /// <summary>Its position in that chain.</summary>
    [Parameter]
    public int LinkIndex { get; set; }

    int BandCount
    {
        get
        {
            int count = 0;

            foreach (ParameterState parameter in Link.Parameters)
            {
                if (parameter.Id.StartsWith("band", StringComparison.Ordinal)
                    && parameter.Id.EndsWith(".frequency", StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }
    }

    double Frequency(int band) => Value(Id(band, "frequency"), 1000);

    double Gain(int band) => Value(Id(band, "gain"), 0);

    double Q(int band) => Value(Id(band, "q"), 1);

    // The engine numbers its bands from one, because that is how an operator counts them. Named in
    // one place so the two never drift apart.
    static string Id(int band, string parameter) => $"band{band + 1}.{parameter}";

    double Value(string id, double fallback)
    {
        foreach (ParameterState parameter in Link.Parameters)
        {
            if (parameter.Id == id)
            {
                return parameter.Value;
            }
        }

        return fallback;
    }

    // Logarithmic, because hearing is. A linear frequency axis spends four fifths of its width above
    // 4 kHz, where a speech equaliser almost never goes.
    static double X(double frequency) =>
        (Math.Log10(Math.Clamp(frequency, 20, 20000)) - Math.Log10(20)) / (Math.Log10(20000) - Math.Log10(20)) * Width;

    static double FromX(double x) =>
        Math.Pow(10, (x / Width * (Math.Log10(20000) - Math.Log10(20))) + Math.Log10(20));

    static double Y(double gainDb) => Height / 2.0 - (Math.Clamp(gainDb, -18, 18) / 18.0 * (Height / 2.0 - 10));

    static double FromY(double y) => (Height / 2.0 - y) / (Height / 2.0 - 10) * 18.0;

    /// <summary>
    /// The summed magnitude response, sampled across the axis.
    /// </summary>
    /// <remarks>
    /// A peaking filter's response near its centre is what an operator is looking at, so this uses
    /// the shape a bell actually has rather than drawing a triangle: gain falls off with the square
    /// of the distance in octaves, scaled by Q. It is a drawing, not a measurement — the engine's
    /// biquads are the truth, and this is close enough to reason with and far cheaper than
    /// evaluating a transfer function at every pixel twice a second.
    /// </remarks>
    string Curve
    {
        get
        {
            System.Text.StringBuilder path = new();
            int bands = BandCount;

            for (int step = 0; step <= 96; step++)
            {
                double x = step / 96.0 * Width;
                double frequency = FromX(x);
                double gain = 0;

                for (int band = 0; band < bands; band++)
                {
                    double octaves = Math.Log2(frequency / Math.Max(Frequency(band), 1));
                    double width = 1.0 / Math.Max(Q(band), 0.2);

                    gain += Gain(band) / (1.0 + Math.Pow(octaves / width, 2) * 4.0);
                }

                path.Append(step == 0 ? 'M' : 'L').Append(F(x)).Append(' ').Append(F(Y(gain))).Append(' ');
            }

            return path.ToString();
        }
    }

    static string Label(double frequency) =>
        frequency >= 1000
            ? (frequency / 1000).ToString("0.#", CultureInfo.InvariantCulture) + "k"
            : frequency.ToString("0", CultureInfo.InvariantCulture);

    static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    async Task OnMoveAsync(PointerEventArgs arguments)
    {
        if (dragging < 0 || isSending)
        {
            return;
        }

        isSending = true;

        try
        {
            // The element is drawn at its viewBox size, so an offset is already a user unit.
            await SendAsync(Id(dragging, "frequency"), FromX(arguments.OffsetX));
            await SendAsync(Id(dragging, "gain"), FromY(arguments.OffsetY));
        }
        finally
        {
            isSending = false;
        }
    }

    async Task SendAsync(string parameterId, double value) =>
        await Session.ApplyAsync(new Command
        {
            SetModifierParameter = new SetModifierParameter
            {
                Target = Target,
                LinkIndex = LinkIndex,
                ParameterId = parameterId,
                Value = value
            }
        });
}
