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

/// <summary>The code behind <c>DriftChart.razor</c>.</summary>
public partial class DriftChart
{
    /// <summary>The samples, oldest first.</summary>
    [Parameter]
    public IReadOnlyList<DriftPoint> Points { get; set; } = [];

    /// <summary>What the channels are called.</summary>
    [Parameter]
    public IReadOnlyList<string> Names { get; set; } = [];

    /// <summary>
    /// What the top and bottom of the chart mean, in parts per million.
    /// </summary>
    /// <remarks>
    /// Fixed rather than fitted to the data. An auto-scaled chart makes a perfectly healthy device
    /// wandering by two parts per million look exactly like one wandering by two hundred, which is
    /// the one distinction anybody opens this panel to make.
    ///
    /// Five hundred because that is FillServo.MaxCorrectionPpm — the edge of what the servo can do
    /// anything about. A device drawn at the top of this chart is a device the correction has run out
    /// of authority for, which is a boundary worth being able to see rather than a round number.
    /// </remarks>
    [Parameter]
    public double Scale { get; set; } = 500.0;

    Dictionary<int, List<DriftPoint>> Series
    {
        get
        {
            Dictionary<int, List<DriftPoint>> series = [];

            foreach (DriftPoint point in Points)
            {
                if (!series.TryGetValue(point.ChannelIndex, out List<DriftPoint>? line))
                {
                    line = [];
                    series[point.ChannelIndex] = line;
                }

                line.Add(point);
            }

            return series;
        }
    }

    IEnumerable<(int Channel, string Path)> Paths
    {
        get
        {
            foreach ((int channel, List<DriftPoint> line) in Series)
            {
                yield return (channel, Build(line));
            }
        }
    }

    string Build(List<DriftPoint> line)
    {
        System.Text.StringBuilder path = new();

        for (int index = 0; index < line.Count; index++)
        {
            double x = line.Count <= 1 ? 0 : index * 320.0 / (line.Count - 1);
            double y = 55 - (Math.Clamp(line[index].DriftPpm / Scale, -1, 1) * 50);

            path.Append(index == 0 ? 'M' : 'L').Append(F(x)).Append(' ').Append(F(y)).Append(' ');
        }

        return path.ToString();
    }

    string NameOf(int channel) => channel < Names.Count ? Names[channel] : $"#{channel}";

    static string Colour(int channel) => StripPalette.For(channel);

    static string F(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
