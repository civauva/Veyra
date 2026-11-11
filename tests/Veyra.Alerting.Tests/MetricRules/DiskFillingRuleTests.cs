using Veyra.Alerting.MetricRules;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.MetricRules;

public class DiskFillingRuleTests
{
    private readonly DiskFillingOptions _options = new();

    [Theory]
    [InlineData(60.0, null)]
    [InlineData(14.1, null)]
    [InlineData(14.0, AlertSeverity.Caution)]
    [InlineData(9.0, AlertSeverity.Caution)]
    [InlineData(3.0, AlertSeverity.Warning)]
    [InlineData(0.5, AlertSeverity.Warning)]
    public void Evaluate_WithoutAnOpenAlert_RaisesFromTheThresholds(double days, AlertSeverity? expected)
    {
        Assert.Equal(expected, DiskFillingRule.Evaluate(days, null, _options));
    }

    [Theory]
    [InlineData(20.0, AlertSeverity.Caution)]
    [InlineData(21.0, AlertSeverity.Caution)]
    [InlineData(21.1, null)]
    public void Evaluate_OpenCaution_ClearsBeyond21Days(double days, AlertSeverity? expected)
    {
        Assert.Equal(expected, DiskFillingRule.Evaluate(days, AlertSeverity.Caution, _options));
    }

    [Theory]
    [InlineData(4.5, AlertSeverity.Warning)]
    [InlineData(4.6, AlertSeverity.Caution)]
    public void Evaluate_OpenWarning_EasesBeyondHalfAgainItsThreshold(double days, AlertSeverity expected)
    {
        Assert.Equal(expected, DiskFillingRule.Evaluate(days, AlertSeverity.Warning, _options));
    }

    [Fact]
    public void Evaluate_DiskNotFilling_Clears()
    {
        Assert.Null(DiskFillingRule.Evaluate(null, AlertSeverity.Warning, _options));
    }
}
