using Microsoft.Extensions.Time.Testing;
using Veyra.Ingestion.Otlp;

namespace Veyra.Ingestion.Tests.Otlp;

public class FailedKeyThrottleTests
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void IsBlocked_BelowTheLimit_ReturnsFalse()
    {
        var throttle = new FailedKeyThrottle(_clock);

        for (var failure = 0; failure < FailedKeyThrottle.MaxFailuresPerMinute - 1; failure++)
        {
            throttle.RecordFailure("10.0.0.1");
        }

        Assert.False(throttle.IsBlocked("10.0.0.1"));
    }

    [Fact]
    public void IsBlocked_AtTheLimit_ReturnsTrueForThatClientOnly()
    {
        var throttle = new FailedKeyThrottle(_clock);

        for (var failure = 0; failure < FailedKeyThrottle.MaxFailuresPerMinute; failure++)
        {
            throttle.RecordFailure("10.0.0.1");
        }

        Assert.True(throttle.IsBlocked("10.0.0.1"));
        Assert.False(throttle.IsBlocked("10.0.0.2"));
    }

    [Fact]
    public void IsBlocked_AfterTheMinute_ReturnsFalseAgain()
    {
        var throttle = new FailedKeyThrottle(_clock);

        for (var failure = 0; failure < FailedKeyThrottle.MaxFailuresPerMinute; failure++)
        {
            throttle.RecordFailure("10.0.0.1");
        }

        _clock.Advance(TimeSpan.FromSeconds(61));

        Assert.False(throttle.IsBlocked("10.0.0.1"));
    }
}
