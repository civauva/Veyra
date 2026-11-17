using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.MetricRules;

public class PostgresRuleTests
{
    private static readonly DatabaseConnectionsOptions _connections = new();
    private static readonly DatabaseWraparoundOptions _wraparound = new();

    [Theory]
    [InlineData(0.50, null, null)]
    [InlineData(0.80, null, AlertSeverity.Caution)]
    [InlineData(0.92, null, AlertSeverity.Warning)]
    [InlineData(0.76, AlertSeverity.Caution, AlertSeverity.Caution)]
    [InlineData(0.74, AlertSeverity.Caution, null)]
    [InlineData(0.86, AlertSeverity.Warning, AlertSeverity.Warning)]
    [InlineData(0.84, AlertSeverity.Warning, AlertSeverity.Caution)]
    public void Connections_RaisesAtItsLevelsAndClearsFivePointsBelow(
        double ratio,
        AlertSeverity? current,
        AlertSeverity? expected)
    {
        Assert.Equal(expected, DatabaseConnectionsRule.Evaluate(ratio, current, _connections));
    }

    [Theory]
    [InlineData(0.30, null, null)]
    [InlineData(0.50, null, AlertSeverity.Caution)]
    [InlineData(0.75, null, AlertSeverity.Warning)]
    [InlineData(0.46, AlertSeverity.Caution, AlertSeverity.Caution)]
    [InlineData(0.44, AlertSeverity.Caution, null)]
    [InlineData(0.71, AlertSeverity.Warning, AlertSeverity.Warning)]
    [InlineData(0.60, AlertSeverity.Warning, AlertSeverity.Caution)]
    [InlineData(0.02, AlertSeverity.Warning, null)]
    public void Wraparound_RaisesAtItsLevelsAndClearsBelowItsClearLevel(
        double ratio,
        AlertSeverity? current,
        AlertSeverity? expected)
    {
        Assert.Equal(expected, DatabaseWraparoundRule.Evaluate(ratio, current, _wraparound));
    }

    [Fact]
    public void ThresholdFor_EachSeverity_IsItsLevel()
    {
        Assert.Equal(
            (0.80, 0.90, 0.50, 0.75),
            (
                DatabaseConnectionsRule.ThresholdFor(AlertSeverity.Caution, _connections),
                DatabaseConnectionsRule.ThresholdFor(AlertSeverity.Warning, _connections),
                DatabaseWraparoundRule.ThresholdFor(AlertSeverity.Caution, _wraparound),
                DatabaseWraparoundRule.ThresholdFor(AlertSeverity.Warning, _wraparound)));
    }
}
