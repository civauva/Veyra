using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Proto.Logs.V1;
using Veyra.Core.Agents;
using Veyra.Core.Security;
using Veyra.Core.Telemetry;
using Veyra.Ingestion.Pipeline;
using Veyra.Testing;
using static Veyra.Ingestion.Tests.Otlp.OtlpRequests;

namespace Veyra.Ingestion.Tests.Pipeline;

public class LogPipelineTests
{
    private static readonly IngestionKeyCheck _anyEnvironment = new(true, Guid.NewGuid(), null);

    [Fact]
    public void Accept_KeyForAllEnvironments_QueuesEveryRecord()
    {
        var queue = Queue(capacity: 10);
        var pipeline = new LogPipeline(queue, Activity(10), new RecordingProbes(), TimeProvider.System);

        var result = pipeline.Accept(Single("payments", "production", Record("a"), Record("b")), _anyEnvironment);

        Assert.Equal(new IngestResult(2, 0, null, QueueFull: false), result);
        Assert.Equal(2, queue.QueuedRecords);
    }

    [Fact]
    public void Accept_KeyLimitedToOtherEnvironment_RejectsItsRecordsWithAReason()
    {
        var queue = Queue(capacity: 10);
        var pipeline = new LogPipeline(queue, Activity(10), new RecordingProbes(), TimeProvider.System);
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
        var pipeline = new LogPipeline(queue, Activity(10), new RecordingProbes(), TimeProvider.System);
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
        new LogPipeline(queue, Activity(10), new RecordingProbes(), TimeProvider.System)
            .Accept(Single("payments", null, Record("a"), Record("b")), _anyEnvironment);
        using var service = Service(queue, writer, new RecordingLive());

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => writer.Written.Count == 2);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(["a", "b"], writer.Written.Select(record => record.Body));
        Assert.Equal(0, queue.QueuedRecords);
    }

    [Fact]
    public async Task WriterService_WrittenBatch_IsHandedToTheLiveStreamsAfterItIsStored()
    {
        var queue = Queue(capacity: 10);
        var writer = new RecordingWriter(failuresBeforeSuccess: 1);
        var live = new RecordingLive();
        new LogPipeline(queue, Activity(10), new RecordingProbes(), TimeProvider.System)
            .Accept(Single("payments", null, Record("a"), Record("b")), _anyEnvironment);
        using var service = Service(queue, writer, live);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => live.Logs.Count == 2, TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        // The failed attempt hands nothing over, so a retried batch is not shown twice.
        Assert.Equal(["a", "b"], live.Logs.Select(record => record.Body));
    }

    [Fact]
    public async Task WriterService_LiveFails_TheBatchStaysWrittenAndIsNotRetried()
    {
        var queue = Queue(capacity: 10);
        var writer = new RecordingWriter(failuresBeforeSuccess: 0);
        new LogPipeline(queue, Activity(10), new RecordingProbes(), TimeProvider.System)
            .Accept(Single("payments", null, Record("a")), _anyEnvironment);
        using var service = Service(queue, writer, new RecordingLive { Fails = true });

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => queue.QueuedRecords == 0);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, writer.Attempts);
    }

    [Fact]
    public async Task WriterService_DatabaseFailsOnce_RetriesTheSameBatch()
    {
        var queue = Queue(capacity: 10);
        var writer = new RecordingWriter(failuresBeforeSuccess: 1);
        new LogPipeline(queue, Activity(10), new RecordingProbes(), TimeProvider.System).Accept(
            Single("payments", null, Record("a")),
            _anyEnvironment);
        using var service = Service(queue, writer, new RecordingLive());

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => writer.Written.Count == 1, TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(2, writer.Attempts);
        Assert.Equal(0, queue.QueuedRecords);
    }

    [Fact]
    public void Accept_RowsOfTheAgentsOwnQueries_GoToTheActivityQueueAndTheRestStaysLogs()
    {
        var queue = Queue(capacity: 10);
        var activity = Activity(10);
        var row = Record("SELECT 1");
        row.Attributes.Add(Attribute("veyra_record", "sqlserver.session"));
        row.Attributes.Add(Attribute("session_id", "57"));

        var result = new LogPipeline(queue, activity, new RecordingProbes(), TimeProvider.System)
            .Accept(Single("payments", null, row, Record("a")), _anyEnvironment);

        Assert.Equal(2, result.Accepted);
        Assert.Equal(1, queue.QueuedRecords);
        Assert.Equal(1, activity.QueuedRecords);
    }

    [Fact]
    public void Accept_ProbeAnswer_GoesToTheProbesWithTheSendersKeyAndNotToTheLogs()
    {
        var queue = Queue(capacity: 10);
        var probes = new RecordingProbes();
        var row = Record("probe");
        row.Attributes.Add(Attribute("veyra_record", "sqlserver.probe"));
        row.Attributes.Add(Attribute("probe", "0195a000000070008000000000000001"));

        var result = new LogPipeline(queue, Activity(10), probes, TimeProvider.System)
            .Accept(Single("otelcol-contrib", null, row, Record("a")), _anyEnvironment);

        Assert.Equal(2, result.Accepted);
        Assert.Equal(1, queue.QueuedRecords);
        var report = Assert.Single(probes.Reports);
        Assert.Equal(_anyEnvironment.KeyId, report.KeyId);
        Assert.Equal("0195a000000070008000000000000001", report.Attributes["probe"]);
    }

    [Fact]
    public void Accept_ProbesRefuse_RejectsTheWholeRequestSoTheSenderRetries()
    {
        var queue = Queue(capacity: 10);
        var row = Record("probe");
        row.Attributes.Add(Attribute("veyra_record", "sqlserver.probe"));

        var result = new LogPipeline(queue, Activity(10), new RecordingProbes { Full = true }, TimeProvider.System)
            .Accept(Single("otelcol-contrib", null, row, Record("a")), _anyEnvironment);

        Assert.True(result.QueueFull);
        Assert.Equal(0, queue.QueuedRecords);
    }

    private static ActivityQueue Activity(int capacity) =>
        new(new TestOptionsMonitor<IngestionOptions>(new IngestionOptions { MaxQueueLength = capacity }));

    private static LogQueue Queue(int capacity) =>
        new(new TestOptionsMonitor<IngestionOptions>(new IngestionOptions { MaxQueueLength = capacity }));

    private static LogWriterService Service(LogQueue queue, ILogWriter writer, ILiveTelemetry live) => new(
        queue,
        writer,
        live,
        TimeProvider.System,
        NullLogger<LogWriterService>.Instance);

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(condition(), "The condition was not met in time.");
    }

    private sealed class RecordingProbes : IAgentProbeReports
    {
        public bool Full { get; init; }

        public List<AgentProbeReport> Reports { get; } = [];

        public bool TryAccept(AgentProbeReport report)
        {
            if (Full)
            {
                return false;
            }

            Reports.Add(report);

            return true;
        }
    }

    private sealed class RecordingLive : ILiveTelemetry
    {
        public bool Fails { get; init; }

        public List<Core.Telemetry.LogRecord> Logs { get; } = [];

        public void LogsWritten(IReadOnlyList<Core.Telemetry.LogRecord> records)
        {
            if (Fails)
            {
                throw new InvalidOperationException("Live is broken.");
            }

            lock (Logs)
            {
                Logs.AddRange(records);
            }
        }

        public void SpansWritten(IReadOnlyList<LiveCount> counts)
        {
        }

        public void PointsWritten(IReadOnlyList<LiveCount> counts)
        {
        }
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
