using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Core.Metrics;
using Veyra.Storage.Metrics;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Metrics;

[Collection(PostgresGroup.Name)]
public class MetricRetentionTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _noon = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ApplyAsync_DropsOldDaysAndForgottenSeriesAndKeepsRecentOnes()
    {
        await using var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var clock = new FakeTimeProvider(_noon.AddDays(-40));
        var options = Options.Create(new StorageOptions { MetricRetentionDays = 30 });
        var store = new MetricStore(database, options, clock, NullLogger<MetricStore>.Instance);
        var partitions = new MetricPartitions(database, clock, NullLogger<MetricPartitions>.Instance);

        // An old series that stopped, then a recent one.
        await partitions.EnsureAsync(_noon.AddDays(-40), default);
        await store.WriteAsync([Point("old", _noon.AddDays(-40))], CancellationToken.None);
        clock.SetUtcNow(_noon);
        await partitions.EnsureAsync(_noon, default);
        await store.WriteAsync([Point("recent", _noon)], CancellationToken.None);

        var (dropped, deleted) = await new MetricRetention(
            database,
            store,
            options,
            clock,
            NullLogger<MetricRetention>.Instance).ApplyAsync(_noon, CancellationToken.None);

        Assert.Contains("metric_point_20261001", dropped);
        Assert.Equal(1, deleted);
        var left = await store.FindSeriesAsync(new MetricSeriesQuery(DateTimeOffset.MinValue), default);
        Assert.Equal("recent", Assert.Single(left).Name);

        // The cache was cleared, so the old series comes back with a new ID instead of a deleted one.
        await store.WriteAsync([Point("old", _noon)], CancellationToken.None);
        Assert.Equal(2, (await store.FindSeriesAsync(new MetricSeriesQuery(DateTimeOffset.MinValue), default)).Count);
    }

    private static MetricPoint Point(string name, DateTimeOffset at) => new(
        Name: name,
        Kind: MetricKind.Gauge,
        Unit: "1",
        Description: string.Empty,
        Temporality: MetricTemporality.Unspecified,
        Monotonic: false,
        ServiceName: null,
        Environment: null,
        HostName: "web-01",
        ResourceAttributes: new Dictionary<string, string> { ["host.name"] = "web-01" },
        Attributes: new Dictionary<string, string>(),
        Timestamp: at,
        StartTimestamp: null,
        Value: 1,
        Histogram: null);
}
