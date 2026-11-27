using Veyra.Query.Metrics;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Tests.Metrics;

/// <summary>The Overview tab's figures are worked out on the server (VEY-68).</summary>
public class DatabaseOverviewTests
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Of_Waits_TellTheFirstAndItsShareOfTheAverageTimeWaited()
    {
        var overview = DatabaseOverview.Of(Server(waits: [Line("CPU", 3, 5), Line("Lock", 1, null), Line("IO", 0, 0)]));

        Assert.Equal(new WaitShareResponse("CPU", 0.8), overview.TopWait);
    }

    [Fact]
    public void Of_NothingWaited_HasNoTopWait()
    {
        Assert.Null(DatabaseOverview.Of(Server(waits: [Line("CPU", 0, 0)])).TopWait);
    }

    [Fact]
    public void Of_Wraparound_IsTheDatabaseClosestToIt()
    {
        DatabasePartResponse[] parts =
        [
            Part("shop") with { WraparoundRatio = 0.2 },
            Part("reporting") with { WraparoundRatio = 0.6 },
            Part("audit"),
        ];

        Assert.Equal("reporting", DatabaseOverview.Of(Server(databases: parts)).OldestWraparound?.Name);
    }

    private static ChartLineResponse Line(string label, double? first, double? second) =>
        new(label, second, [new(_noon, first), new(_noon.AddMinutes(1), second)]);

    private static DatabasePartResponse Part(string name) => new(name, null, null, null, 3600, null, null);

    private static DatabaseMetricsResponse Server(
        IReadOnlyList<ChartLineResponse>? waits = null,
        IReadOnlyList<DatabasePartResponse>? databases = null)
    {
        var empty = new ChartLineResponse("", null, []);

        return new DatabaseMetricsResponse(
            null!,
            "1h",
            60,
            empty,
            empty,
            empty,
            empty,
            empty,
            empty,
            waits ?? [],
            databases ?? [],
            [],
            [],
            []);
    }
}
