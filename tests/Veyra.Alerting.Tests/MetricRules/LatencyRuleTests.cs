using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.MetricRules;

public class LatencyRuleTests
{
    private readonly LatencyOptions _options = new();

    [Theory]
    [InlineData(0.4, null)]
    [InlineData(1.0, AlertSeverity.Caution)]
    [InlineData(2.9, AlertSeverity.Caution)]
    [InlineData(3.0, AlertSeverity.Warning)]
    public void Evaluate_EnoughRequests_RaisesFromTheThresholds(double p95, AlertSeverity? expected)
    {
        Assert.Equal(expected, LatencyRule.Evaluate(p95, 100, null, _options));
    }

    [Theory]
    [InlineData(0.8, AlertSeverity.Caution)]
    [InlineData(0.79, null)]
    public void Evaluate_OpenCaution_ClearsBelow80PercentOfTheThreshold(double p95, AlertSeverity? expected)
    {
        Assert.Equal(expected, LatencyRule.Evaluate(p95, 100, AlertSeverity.Caution, _options));
    }

    [Theory]
    [InlineData(2.41, AlertSeverity.Warning)]
    [InlineData(2.39, AlertSeverity.Caution)]
    public void Evaluate_OpenWarning_EasesBelow80PercentOfItsThreshold(double p95, AlertSeverity expected)
    {
        Assert.Equal(expected, LatencyRule.Evaluate(p95, 100, AlertSeverity.Warning, _options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(AlertSeverity.Warning)]
    public void Evaluate_TooFewRequests_KeepsTheCurrentState(AlertSeverity? current)
    {
        Assert.Equal(current, LatencyRule.Evaluate(9.0, 19, current, _options));
        Assert.Equal(current, LatencyRule.Evaluate(0.1, 19, current, _options));
    }

    [Fact]
    public void Evaluate_NoPercentile_KeepsTheCurrentState()
    {
        Assert.Equal(AlertSeverity.Caution, LatencyRule.Evaluate(null, 500, AlertSeverity.Caution, _options));
    }
}
