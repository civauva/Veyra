using Veyra.Alerting.ErrorRates;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Tests.ErrorRates;

public class ErrorRateRuleTests
{
    private static readonly ErrorRateOptions _options = new();

    [Theory]
    [InlineData(100, 0)]
    [InlineData(100, 0.9)]
    public void Evaluate_LowRatioWithoutAlert_OpensNothing(long total, double percent)
    {
        Assert.Null(ErrorRateRule.Evaluate(total, Errors(total, percent), current: null, _options));
    }

    [Fact]
    public void Evaluate_AtCautionThreshold_OpensCaution()
    {
        Assert.Equal(AlertSeverity.Caution, ErrorRateRule.Evaluate(100, 1, current: null, _options));
    }

    [Fact]
    public void Evaluate_AtWarningThreshold_OpensWarning()
    {
        Assert.Equal(AlertSeverity.Warning, ErrorRateRule.Evaluate(100, 5, current: null, _options));
    }

    [Fact]
    public void Evaluate_FewerRecordsThanMinimum_OpensNothing()
    {
        // 10 errors out of 19 is 53 %, but 19 records are too few to judge.
        Assert.Null(ErrorRateRule.Evaluate(19, 10, current: null, _options));
    }

    [Theory]
    [InlineData(AlertSeverity.Caution)]
    [InlineData(AlertSeverity.Warning)]
    public void Evaluate_FewerRecordsThanMinimum_KeepsOpenAlert(AlertSeverity current)
    {
        Assert.Equal(current, ErrorRateRule.Evaluate(5, 0, current, _options));
    }

    [Fact]
    public void Evaluate_WarningAboveHalfItsThreshold_StaysWarning()
    {
        // 3 % is below the 5 % warning threshold, but not below half of it, so the alert does not flicker.
        Assert.Equal(AlertSeverity.Warning, ErrorRateRule.Evaluate(100, 3, AlertSeverity.Warning, _options));
    }

    [Fact]
    public void Evaluate_WarningBelowHalfItsThreshold_FallsToCaution()
    {
        Assert.Equal(AlertSeverity.Caution, ErrorRateRule.Evaluate(1000, 20, AlertSeverity.Warning, _options));
    }

    [Fact]
    public void Evaluate_CautionAboveHalfItsThreshold_StaysCaution()
    {
        Assert.Equal(AlertSeverity.Caution, ErrorRateRule.Evaluate(1000, 6, AlertSeverity.Caution, _options));
    }

    [Theory]
    [InlineData(AlertSeverity.Caution)]
    [InlineData(AlertSeverity.Warning)]
    public void Evaluate_BelowHalfTheCautionThreshold_Resolves(AlertSeverity current)
    {
        Assert.Null(ErrorRateRule.Evaluate(1000, 4, current, _options));
    }

    [Fact]
    public void Evaluate_CautionReachingWarningThreshold_RisesToWarning()
    {
        Assert.Equal(AlertSeverity.Warning, ErrorRateRule.Evaluate(100, 6, AlertSeverity.Caution, _options));
    }

    [Fact]
    public void Evaluate_CustomThresholds_AreUsed()
    {
        var strict = new ErrorRateOptions { CautionRatio = 0.001, WarningRatio = 0.01, MinimumRecords = 1 };

        Assert.Equal(AlertSeverity.Warning, ErrorRateRule.Evaluate(100, 1, current: null, strict));
    }

    [Fact]
    public void ThresholdFor_ReturnsTheSeveritysRatio()
    {
        Assert.Equal(0.01, ErrorRateRule.ThresholdFor(AlertSeverity.Caution, _options));
        Assert.Equal(0.05, ErrorRateRule.ThresholdFor(AlertSeverity.Warning, _options));
    }

    private static long Errors(long total, double percent) => (long)(total * percent / 100);
}
