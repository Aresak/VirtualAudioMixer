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

/// <summary>The code behind <c>Histogram.razor</c>.</summary>
public partial class Histogram
{
    /// <summary>The counts, lowest bucket first.</summary>
    [Parameter]
    public IReadOnlyList<long> Buckets { get; set; } = [];

    /// <summary>How wide each bucket is, as a fraction of a block.</summary>
    [Parameter]
    public double BucketWidth { get; set; } = 0.05;

    long Peak
    {
        get
        {
            long peak = 0;

            foreach (long count in Buckets)
            {
                if (count > peak)
                {
                    peak = count;
                }
            }

            return peak;
        }
    }

    double Boundary => BucketWidth <= 0 || Buckets.Count == 0
        ? 320
        : Math.Min(1.0 / BucketWidth * (320.0 / Buckets.Count), 320);

    string Fill(int index) =>
        BucketWidth > 0 && index * BucketWidth >= 1.0 ? "var(--bad)" : "var(--brass-dim)";

    static string F(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
