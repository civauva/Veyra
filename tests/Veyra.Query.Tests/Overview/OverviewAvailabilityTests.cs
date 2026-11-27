using Veyra.Core.Alerts;
using Veyra.Query.Overview;

namespace Veyra.Query.Tests.Overview;

/// <summary>Availability is the share of time no service had a warning open (ADR 0023, point 3).</summary>
public class OverviewAvailabilityTests
{
    private static readonly DateTimeOffset _end = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset _start = _end.AddDays(-30);

    [Fact]
    public void Ratio_NoAlerts_IsWhole()
    {
        Assert.Equal(1, OverviewAvailability.Ratio([], null, _start, _end));
    }

    [Fact]
    public void Ratio_OverlappingWarnings_CountTheirTimeOnce()
    {
        // Two services down at the same time for part of it: 3 hours in all, not 4.
        AlertSpell[] spells =
        [
            Warning("orders", _start.AddDays(1), _start.AddDays(1).AddHours(2)),
            Warning("billing", _start.AddDays(1).AddHours(1), _start.AddDays(1).AddHours(3)),
        ];

        var ratio = OverviewAvailability.Ratio(spells, null, _start, _end);

        Assert.Equal(1 - (3.0 / (30 * 24)), ratio, 9);
    }

    [Fact]
    public void Ratio_CautionsAndOtherBlocks_DoNotCount()
    {
        AlertSpell[] spells =
        [
            Warning("orders", _start.AddDays(1), _start.AddDays(2)) with { Severity = AlertSeverity.Caution },
            Warning("web-01", _start.AddDays(1), _start.AddDays(2)) with { Subject = AlertSubject.Host("web-01", "/") },
            Warning("sql", _start.AddDays(1), _start.AddDays(2)) with { Subject = AlertSubject.Database("sql") },
        ];

        Assert.Equal(1, OverviewAvailability.Ratio(spells, null, _start, _end));
    }

    [Fact]
    public void Ratio_WarningsReachingOutsideThePeriodOrStillOpen_AreCutAtItsEdges()
    {
        AlertSpell[] spells =
        [
            Warning("orders", _start.AddHours(-5), _start.AddHours(6)),
            Warning("billing", _end.AddHours(-6), null),
        ];

        var ratio = OverviewAvailability.Ratio(spells, null, _start, _end);

        Assert.Equal(1 - (12.0 / (30 * 24)), ratio, 9);
    }

    [Fact]
    public void Ratio_Environment_CountsOnlyItsServices()
    {
        AlertSpell[] spells =
        [
            Warning("orders", _start.AddDays(1), _start.AddDays(2)) with { Environment = "staging" },
            Warning("orders", _start.AddDays(3), _start.AddDays(3).AddHours(6)),
        ];

        var ratio = OverviewAvailability.Ratio(spells, "production", _start, _end);

        Assert.Equal(1 - (6.0 / (30 * 24)), ratio, 9);
    }

    [Fact]
    public void Figure_ComparesTheLastThirtyDaysWithTheThirtyBefore()
    {
        AlertSpell[] spells =
        [
            Warning("orders", _start.AddDays(-10), _start.AddDays(-10).AddHours(36)),
            Warning("orders", _start.AddDays(5), _start.AddDays(5).AddHours(12)),
        ];

        var figure = OverviewAvailability.Figure(spells, null, _end, anyServices: true);

        Assert.Equal(1 - (12.0 / 720), figure.Ratio!.Value, 9);
        Assert.Equal((36.0 - 12.0) / 720, figure.Change!.Value, 9);
        Assert.Equal(30, figure.Days);
    }

    [Fact]
    public void Figure_NoServices_IsUnknown()
    {
        var figure = OverviewAvailability.Figure([], null, _end, anyServices: false);

        Assert.Equal((null, null, 30), (figure.Ratio, figure.Change, figure.Days));
    }

    private static AlertSpell Warning(string service, DateTimeOffset from, DateTimeOffset? to) => new(
        Guid.CreateVersion7(),
        "production",
        AlertSubject.Service(service),
        AlertSeverity.Warning,
        from,
        to);
}
