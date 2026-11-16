using Veyra.Core.Telemetry;
using Veyra.Ingestion.Pipeline;
using Veyra.Testing;

namespace Veyra.Ingestion.Tests.Pipeline;

/// <summary>A queue's limit follows the Settings screen without a restart (ADR 0017).</summary>
public sealed class QueueLimitTests
{
    [Fact]
    public void TryEnqueue_LimitRaisedWhileRunning_TakesEffectAtOnce()
    {
        var options = new TestOptionsMonitor<IngestionOptions>(new IngestionOptions { MaxQueueLength = 2 });
        var queue = new LogQueue(options);
        Assert.True(queue.TryEnqueue([Record(), Record()]));
        Assert.False(queue.TryEnqueue([Record()]));

        options.Set(new IngestionOptions { MaxQueueLength = 3 });

        Assert.True(queue.TryEnqueue([Record()]));
    }

    [Fact]
    public void TryEnqueue_LimitLoweredBelowWhatIsQueued_RefusesUntilWritten()
    {
        var options = new TestOptionsMonitor<IngestionOptions>(new IngestionOptions { MaxQueueLength = 5 });
        var queue = new LogQueue(options);
        Assert.True(queue.TryEnqueue([Record(), Record(), Record()]));

        options.Set(new IngestionOptions { MaxQueueLength = 2 });

        Assert.False(queue.TryEnqueue([Record()]));
        queue.MarkWritten(3);
        Assert.True(queue.TryEnqueue([Record()]));
    }

    private static LogRecord Record() => new(
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        LogSeverity.Information,
        null,
        "queued",
        "payments",
        null,
        null,
        null,
        new Dictionary<string, string>(),
        new Dictionary<string, string>());
}
