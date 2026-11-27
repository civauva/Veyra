using Veyra.Alerting.Alerts;
using Veyra.Alerting.Api;
using Veyra.Alerting.Incidents;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.Incidents;

public sealed class IncidentItemResponseTests
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Of_TwoDisksOfAHostAndTwoContainersOfAName_ListsWholeBlocksOnce()
    {
        var incident = new Incident(
            Guid.CreateVersion7(),
            42,
            "production",
            "disk / on web-01 is 92% full",
            AlertSeverity.Caution,
            IncidentStatus.Open,
            _noon,
            null,
            null,
            null,
            null,
            _noon);

        var item = IncidentItemResponse.Of(
            incident,
            [
                Entry(AlertSubject.Host("web-01", "/")),
                Entry(AlertSubject.Host("web-01", "/var")),
                Entry(AlertSubject.Container("shop-api", "web-01")),
                Entry(AlertSubject.Container("shop-api", "web-02")),
            ]);

        Assert.Equal(("INC-42", "open", 4), (item.Key, item.State, item.AlertCount));
        Assert.Equal(
            [("host", (string?)null), ("container", "web-01"), ("container", "web-02")],
            item.Blocks.Select(block => (block.Kind, block.Part)));
    }

    private static AlertListEntry Entry(AlertSubject subject) => new(
        new AlertState(
            Guid.CreateVersion7(),
            AlertRules.DiskSpace,
            "production",
            subject,
            AlertSeverity.Caution,
            0.92,
            0.92,
            0.9,
            TimeSpan.FromMinutes(5),
            _noon,
            _noon,
            null),
        AlertSeverity.Caution,
        null);
}
