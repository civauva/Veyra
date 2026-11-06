using Veyra.Core.Telemetry;

namespace Veyra.Core.Tests.Telemetry;

public class LogSeverityTests
{
    [Theory]
    [InlineData(1, LogSeverity.Trace)]
    [InlineData(4, LogSeverity.Trace)]
    [InlineData(5, LogSeverity.Debug)]
    [InlineData(9, LogSeverity.Information)]
    [InlineData(13, LogSeverity.Warning)]
    [InlineData(17, LogSeverity.Error)]
    [InlineData(21, LogSeverity.Fatal)]
    [InlineData(24, LogSeverity.Fatal)]
    public void FromOtlpSeverityNumber_InRange_MapsToExpectedSeverity(int number, LogSeverity expected)
    {
        Assert.Equal(expected, LogSeverityExtensions.FromOtlpSeverityNumber(number));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(-1)]
    public void FromOtlpSeverityNumber_OutOfRange_MapsToUnspecified(int number)
    {
        Assert.Equal(LogSeverity.Unspecified, LogSeverityExtensions.FromOtlpSeverityNumber(number));
    }
}
