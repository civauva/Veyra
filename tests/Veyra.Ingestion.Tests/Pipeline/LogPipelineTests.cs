using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Logs.V1;
using Veyra.Core.Security;
using Veyra.Core.Telemetry;
using Veyra.Ingestion.Pipeline;
using static Veyra.Ingestion.Tests.Otlp.OtlpRequests;

namespace Veyra.Ingestion.Tests.Pipeline;

public class LogPipelineTests
{
    private static readonly IngestionKeyCheck _anyEnvironment = new(true, Guid.NewGuid(), null);

    [Fact]
    public void Accept_KeyForAllEnvironments_QueuesEveryRecord()
    {
        var queue = Queue(capacity: 10);
        var pipeline = new LogPipeline(queue, TimeProvider.System);

        var result = pipeline.Accept(Single("payments", "production", Record("a"), Record("b")), _anyEnvironment);

        Assert.Equal(new IngestResult(2, 0, null, QueueFull: false), result);
        Assert.Equal(2, queue.QueuedRecords);
    }

    [Fact]
    public void Accept_KeyLimitedToOtherEnvironment_RejectsItsRecordsWithAReason()
    {
        var queue = Queue(capacity: 10);
        var pipeline = new LogPipeline(queue, TimeProvider.System);
        var stagingOnly = new IngestionKeyCheck(true, Guid.NewGuid(), ["staging"]);
        var request = Single("payments", "production", Record("a"));
        request.ResourceLogs.Add(Single("payments", "staging", Record("b")).ResourceLogs[0]);

        var result = pipeline.Accept(request, stagingOnly);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(1, result.Rejected);
        Assert.Contains("production", result.RejectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_QueueFull_RejectsTheWholeRequestSoTheSenderRetries()
    {
        var queue = Queue(capacity: 2);
        var pipeline = new LogPipeline(queue, TimeProvider.System);
        pipeline.Accept(Single("payments", null, Record("a"), Record("b")), _anyEnvironment);

        var result = pipeline.Accept(Single("payments", null, Record("c")), _anyEnvironment);

        Assert.True(result.QueueFull);
        Assert.Equal(2, queue.QueuedRecords);
    }

    [Fact]
    public async Task WriterService_QueuedRecords_AreWrittenAndFreeTheQueue()
    {
        var queue = Queue(capacity: 10);
        var writer = new RecordingWriter(failuresBeforeSuccess: 0);
        new LogPipeline(queue, TimeProvider.System)
            .Accept(Single("payments", null, Record("a"), Record("b")), _anyEnvironment);
        using var service =
            new LogWriterService(queue, writer, TimeProvider.System, NullLogger<LogWriterService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => writer.Written.Count == 2);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(["a", "b"], writer.Written.Select(record => record.Body));
        Assert.Equal(0, queue.QueuedRecords);
    }

    [Fact]
    public async Task WriterService_DatabaseFailsOnce_RetriesTheSameBatch()
    {
        var queue = Queue(capacity: 10);
        var writer = new RecordingWriter(failuresBeforeSuccess: 1);
        new LogPipeline(queue, TimeProvider.System).Accept(Single("payments", null, Record("a")), _anyEnvironment);
        using var service =
            new LogWriterService(queue, writer, TimeProvider.System, NullLogger<LogWriterService>.Instance);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => writer.Written.Count == 1, TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, writer.Attempts);
        Assert.Equal(0, queue.QueuedRecords);
    }

    private static LogQueue Queue(int capacity) =>
        new(Options.Create(new IngestionOptions { MaxQueueLength = capacity }));

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "The condition was not met in time.");
    }

    private sealed class RecordingWriter(int failuresBeforeSuccess) : ILogWriter
    {
        private int _failuresLeft = failuresBeforeSuccess;

        public List<Core.Telemetry.LogRecord> Written { get; } = [];

        public int Attempts { get; private set; }

        public Task WriteAsync(IReadOnlyList<Core.Telemetry.LogRecord> records, CancellationToken cancellationToken)
        {
            Attempts++;

            if (_failuresLeft-- > 0)
            {
                throw new InvalidOperationException("Database unavailable.");
            }

            lock (Written)
            {
                Written.AddRange(records);
            }

            return Task.CompletedTask;
        }
    }
}
