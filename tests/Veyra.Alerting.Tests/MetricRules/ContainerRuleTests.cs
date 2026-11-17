using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.MetricRules;

public class ContainerRuleTests
{
    private static readonly ContainerRestartsOptions _restarts = new();
    private static readonly ContainerMemoryOptions _memory = new();

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(1, null, null)]
    [InlineData(2, null, AlertSeverity.Caution)]
    [InlineData(5, null, AlertSeverity.Warning)]
    [InlineData(1, AlertSeverity.Warning, AlertSeverity.Warning)]
    [InlineData(3, AlertSeverity.Warning, AlertSeverity.Warning)]
    [InlineData(6, AlertSeverity.Caution, AlertSeverity.Warning)]
    [InlineData(0, AlertSeverity.Caution, null)]
    [InlineData(0, AlertSeverity.Warning, null)]
    public void Restarts_FifteenMinutesRaiseAndFifteenMinutesWithoutOneClear(
        double restarts,
        AlertSeverity? current,
        AlertSeverity? expected)
    {
        Assert.Equal(expected, ContainerRestartsRule.Evaluate(restarts, current, _restarts));
    }

    [Theory]
    [InlineData(0.50, null, null)]
    [InlineData(0.90, null, AlertSeverity.Caution)]
    [InlineData(0.96, null, AlertSeverity.Warning)]
    [InlineData(0.86, AlertSeverity.Caution, AlertSeverity.Caution)]
    [InlineData(0.84, AlertSeverity.Caution, null)]
    [InlineData(0.91, AlertSeverity.Warning, AlertSeverity.Warning)]
    [InlineData(0.89, AlertSeverity.Warning, AlertSeverity.Caution)]
    public void Memory_RaisesAtItsLevelsAndClearsFivePointsBelow(
        double ratio,
        AlertSeverity? current,
        AlertSeverity? expected)
    {
        Assert.Equal(expected, ContainerMemoryRule.Evaluate(ratio, current, _memory));
    }

    [Fact]
    public void ThresholdFor_EachSeverity_IsItsLevel()
    {
        Assert.Equal(
            (2d, 5d, 0.90, 0.95),
            (
                ContainerRestartsRule.ThresholdFor(AlertSeverity.Caution, _restarts),
                ContainerRestartsRule.ThresholdFor(AlertSeverity.Warning, _restarts),
                ContainerMemoryRule.ThresholdFor(AlertSeverity.Caution, _memory),
                ContainerMemoryRule.ThresholdFor(AlertSeverity.Warning, _memory)));
    }
}
