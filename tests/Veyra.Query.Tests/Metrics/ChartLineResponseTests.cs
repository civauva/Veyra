using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Tests.Metrics;

/// <summary>The figures of a chart line, worked out on the server from its points (ADR 0029).</summary>
public sealed class ChartLineResponseTests
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Figures_PointsWithGaps_AreThoseOfTheValuesOnly()
    {
        var line = Line(1.5, null, 4.5, 3, null);

        Assert.Equal((3.0, 4.5), (line.Average!.Value, line.Highest!.Value));
        Assert.Equal(3, line.Latest);
    }

    [Fact]
    public void Figures_NoValue_AreNull()
    {
        var line = Line(null, null);

        Assert.Equal((null, null), (line.Average, line.Highest));
    }

    [Fact]
    public void Figures_NegativeValues_KeepTheHighest()
    {
        var line = Line(-4, -1, -2);

        Assert.Equal((-7.0 / 3, -1.0), (line.Average!.Value, line.Highest!.Value));
    }

    private static ChartLineResponse Line(params double?[] values)
    {
        var points = values.Select((value, i) => new ChartPointResponse(_noon.AddMinutes(i), value)).ToList();

        return new ChartLineResponse("cpu", points.LastOrDefault(point => point.Value is not null)?.Value, points);
    }
}
