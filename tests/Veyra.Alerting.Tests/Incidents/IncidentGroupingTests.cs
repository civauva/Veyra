using Veyra.Alerting.Incidents;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.Incidents;

public sealed class IncidentGroupingTests
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan _window = TimeSpan.FromMinutes(15);
    private static readonly Guid _first = Guid.CreateVersion7();
    private static readonly Guid _second = Guid.CreateVersion7();

    private static readonly BlockLink[] _links =
    [
        new(AlertBlockKind.Service, "checkout", "production", "web-01"),
    ];

    [Fact]
    public void Choose_NoCandidates_ReturnsNone()
    {
        Assert.Null(IncidentGrouping.Choose(Alert(AlertSubject.Service("checkout")), [], _links, _window));
    }

    [Fact]
    public void Choose_RelatedAlertWithinTheWindow_JoinsItsIncident()
    {
        var candidates = new[] { Candidate(_first, AlertSubject.Host("web-01", "/"), _noon.AddMinutes(-10)) };

        Assert.Equal(_first, IncidentGrouping.Choose(Alert(AlertSubject.Service("checkout")), candidates, _links, _window));
    }

    [Fact]
    public void Choose_RelatedAlertBeforeTheWindow_ReturnsNone()
    {
        var candidates = new[] { Candidate(_first, AlertSubject.Host("web-01", "/"), _noon.AddMinutes(-16)) };

        Assert.Null(IncidentGrouping.Choose(Alert(AlertSubject.Service("checkout")), candidates, _links, _window));
    }

    [Fact]
    public void Choose_UnrelatedAlert_ReturnsNone()
    {
        var candidates = new[] { Candidate(_first, AlertSubject.Service("billing"), _noon.AddMinutes(-1)) };

        Assert.Null(IncidentGrouping.Choose(Alert(AlertSubject.Service("checkout")), candidates, _links, _window));
    }

    [Fact]
    public void Choose_TwoRelatedIncidents_JoinsTheOneWithTheNewestRelatedAlert()
    {
        var candidates = new[]
        {
            Candidate(_first, AlertSubject.Service("checkout"), _noon.AddMinutes(-12)),
            Candidate(_second, AlertSubject.Container("checkout-api", "web-01"), _noon.AddMinutes(-2)),
        };

        Assert.Equal(_second, IncidentGrouping.Choose(Alert(AlertSubject.Host("web-01")), candidates, _links, _window));
    }

    private static AlertState Alert(AlertSubject subject) => new(
        Guid.CreateVersion7(),
        "error-rate",
        "production",
        subject,
        AlertSeverity.Warning,
        0.2,
        0.2,
        0.05,
        TimeSpan.FromMinutes(5),
        _noon,
        _noon,
        null);

    private static IncidentCandidate Candidate(Guid incident, AlertSubject subject, DateTimeOffset openedAt) =>
        new(incident, AlertSeverity.Caution, subject, openedAt);
}
