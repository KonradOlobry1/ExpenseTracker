using MudBlazor;

namespace ExpenseTracker.Presentation.Charts;

public static class ChartDefaults
{
    /// <summary>
    /// Y-axis options for the money charts.
    /// </summary>
    /// <remarks>
    /// Two traps this avoids:
    /// <para>
    /// <see cref="ChartOptions.YAxisTicks"/> is the <em>spacing between</em> ticks, not the
    /// number of them. Setting it to a small number on a series that reaches into the
    /// thousands asks for hundreds of gridlines, which MudBlazor then thins to an arbitrary
    /// count at an arbitrary step. Leave the spacing alone and cap the count instead.
    /// </para>
    /// <para>
    /// A currency format ("C0" produces "$1,024") is far wider than the axis gutter, and
    /// the overflow is clipped from the left — "$1,024" renders as "024". Plain "0" drops
    /// both the symbol and the thousands separator; the chart title already says these are
    /// amounts. app.css shrinks the label font to buy the remaining room.
    /// </para>
    /// </remarks>
    /// <param name="maxValue">
    /// Largest value plotted. The axis gutter fits roughly five glyphs, so anything from
    /// five digits up is labelled in thousands instead of being clipped.
    /// </param>
    public static ChartOptions Money(double maxValue = 0, int maxYAxisTicks = 5) => new()
    {
        MaxNumYAxisTicks = maxYAxisTicks,
        YAxisFormat = maxValue >= 10_000 ? "0,'k'" : "0",
        YAxisLines = true,
        XAxisLines = false,
    };
}
