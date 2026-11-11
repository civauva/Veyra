using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.MetricRules;

public class DiskSpaceRuleTests
{
    private readonly DiskSpaceOptions _options = new();

    [Theory]
    [InlineData(0.5, null)]
    [InlineData(0.8999, null)]
    [InlineData(0.90, AlertSeverity.Caution)]
    [InlineData(0.94, AlertSeverity.Caution)]
    [InlineData(0.95, AlertSeverity.Warning)]
    [InlineData(1.0, AlertSeverity.Warning)]
    public void Evaluate_WithoutAnOpenAlert_RaisesFromTheThresholds(double ratio, AlertSeverity? expected)
    {
        Assert.Equal(expected, DiskSpaceRule.Evaluate(ratio, null, _options));
    }

    [Theory]
    [InlineData(0.86, AlertSeverity.Caution)]
    [InlineData(0.8501, AlertSeverity.Caution)]
    [InlineData(0.8499, null)]
    public void Evaluate_OpenCaution_ClearsFivePointsBelowTheThreshold(double ratio, AlertSeverity? expected)
    {
        Assert.Equal(expected, DiskSpaceRule.Evaluate(ratio, AlertSeverity.Caution, _options));
    }

    [Theory]
    [InlineData(0.91, AlertSeverity.Warning)]
    [InlineData(0.8999, AlertSeverity.Caution)]
    public void Evaluate_OpenWarning_EasesFivePointsBelowItsThreshold(double ratio, AlertSeverity expected)
    {
        Assert.Equal(expected, DiskSpaceRule.Evaluate(ratio, AlertSeverity.Warning, _options));
    }

    [Fact]
    public void ThresholdFor_ReturnsTheConfiguredShare()
    {
        Assert.Equal(0.90, DiskSpaceRule.ThresholdFor(AlertSeverity.Caution, _options));
        Assert.Equal(0.95, DiskSpaceRule.ThresholdFor(AlertSeverity.Warning, _options));
    }
}
