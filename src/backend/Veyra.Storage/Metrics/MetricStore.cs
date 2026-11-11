using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Veyra.Core.Metrics;

namespace Veyra.Storage.Metrics;

/// <summary>
/// Writes metric points to <c>storage.metric_series</c> and <c>storage.metric_point</c>, and reads them back
/// (ADR 0010).
/// </summary>
/// <remarks>
/// Series IDs are cached in memory, so a steady stream of points needs no series lookups at all. The cache holds at
/// most the series limit. Retention clears it after deleting series, so no point is ever written for a deleted one.
/// </remarks>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
/// <param name="options">The Storage settings with the series limit and retention.</param>
/// <param name="clock">The clock for first and last seen times.</param>
/// <param name="logger">Receives a warning when points are dropped at the series limit.</param>
internal sealed partial class MetricStore(
    NpgsqlDataSource dataSource,
    IOptions<StorageOptions> options,
    TimeProvider clock,
    ILogger<MetricStore> logger) : IMetricWriter, IMetricSearch
{
    private const string _copyCommand =
        """
        COPY storage.metric_point
            (series_id, timestamp, start_timestamp, value, count, sum, min, max, bounds, bucket_counts)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string _seriesColumns =
        "id, name, kind, unit, description, temporality, monotonic, service_name, environment, host_name, "
        + "resource_attributes::text, attributes::text, first_seen, last_seen";

    // How often a series' last-seen time is refreshed while points keep arriving.
    private static readonly TimeSpan _lastSeenRefresh = TimeSpan.FromMinutes(1);

    // How often the warning about dropped points may repeat.
    private static readonly TimeSpan _dropWarningInterval = TimeSpan.FromMinutes(5);

    // The point rate covers this much time.
    private static readonly TimeSpan _rateWindow = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, CachedSeries> _known = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<(DateTimeOffset At, int Points)> _written = new();

    private readonly Lock _limitLock = new();

    private int? _seriesCount;

    private DateTimeOffset _lastDropWarning = DateTimeOffset.MinValue;

    /// <inheritdoc />
    public async Task<MetricWriteResult> WriteAsync(
        IReadOnlyList<MetricPoint> points,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return new MetricWriteResult(0, 0);
        }

        var now = clock.GetUtcNow();
        var hashes = points.Select(MetricSeriesIdentity.Of).ToArray();
        var keys = hashes.Select(Convert.ToHexString).ToArray();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        await ResolveSeriesAsync(connection, points, hashes, keys, now, cancellationToken);
        await RefreshLastSeenAsync(connection, keys, now, cancellationToken);

        var stored = 0;
        var droppedNames = new SortedSet<string>(StringComparer.Ordinal);

        await using (var writer = await connection.BeginBinaryImportAsync(_copyCommand, cancellationToken))
        {
            for (var index = 0; index < points.Count; index++)
            {
                if (!_known.TryGetValue(keys[index], out var series))
                {
                    droppedNames.Add(points[index].Name);

                    continue;
                }

                await WritePointAsync(writer, series.Id, points[index], cancellationToken);
                stored++;
            }

            await writer.CompleteAsync(cancellationToken);
        }

        _written.Enqueue((now, stored));

        var dropped = points.Count - stored;

        if (dropped > 0)
        {
            WarnAboutDropped(dropped, droppedNames, now);
        }

        return new MetricWriteResult(stored, dropped);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricSeries>> FindSeriesAsync(
        MetricSeriesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        await using var command = dataSource.CreateCommand();
        command.Parameters.AddWithValue(query.SeenSince);

        var sql = new StringBuilder($"SELECT {_seriesColumns} FROM storage.metric_series WHERE last_seen >= $1");

        if (query.Names is { Count: > 0 } names)
        {
            command.Parameters.AddWithValue(names.ToArray());
            sql.Append(CultureInfo.InvariantCulture, $" AND name = ANY(${command.Parameters.Count})");
        }

        AppendEquals(sql, command, "service_name", query.ServiceName);
        AppendEquals(sql, command, "environment", query.Environment);
        AppendEquals(sql, command, "host_name", query.HostName);

        command.Parameters.AddWithValue(query.Limit);
        sql.Append(CultureInfo.InvariantCulture, $" ORDER BY name, id LIMIT ${command.Parameters.Count}");
        command.CommandText = sql.ToString();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var series = new List<MetricSeries>();

        while (await reader.ReadAsync(cancellationToken))
        {
            series.Add(new MetricSeries(
                Id: reader.GetInt64(0),
                Name: reader.GetString(1),
                Kind: (MetricKind)reader.GetInt16(2),
                Unit: reader.GetString(3),
                Description: reader.GetString(4),
                Temporality: (MetricTemporality)reader.GetInt16(5),
                Monotonic: reader.GetBoolean(6),
                ServiceName: reader.IsDBNull(7) ? null : reader.GetString(7),
                Environment: reader.IsDBNull(8) ? null : reader.GetString(8),
                HostName: reader.IsDBNull(9) ? null : reader.GetString(9),
                ResourceAttributes: Deserialize(reader.GetString(10)),
                Attributes: Deserialize(reader.GetString(11)),
                FirstSeen: reader.GetFieldValue<DateTimeOffset>(12),
                LastSeen: reader.GetFieldValue<DateTimeOffset>(13)));
        }

        return series;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricBucket>> ReadBucketsAsync(
        IReadOnlyList<long> seriesIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        TimeSpan bucketSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seriesIds);

        if (seriesIds.Count == 0)
        {
            return [];
        }

        // One row per series and bucket: the last point as stored, with the average and total of all its points.
        // date_bin aligns the buckets to the start of the window, so they line up with the chart's axis.
        await using var command = dataSource.CreateCommand(
            """
            WITH binned AS (
                SELECT series_id, date_bin($4, timestamp, $2) AS bucket, timestamp, start_timestamp, value, count, sum,
                       min, max, bounds, bucket_counts
                FROM storage.metric_point
                WHERE series_id = ANY($1) AND timestamp >= $2 AND timestamp < $3)
            SELECT DISTINCT ON (series_id, bucket)
                   series_id, bucket,
                   count(*) OVER whole, avg(value) OVER whole, sum(value) OVER whole,
                   timestamp, start_timestamp, value, count, sum, min, max, bounds, bucket_counts
            FROM binned
            WINDOW whole AS (PARTITION BY series_id, bucket)
            ORDER BY series_id, bucket, timestamp DESC
            """);
        command.Parameters.AddWithValue(seriesIds.ToArray());
        command.Parameters.AddWithValue(windowStart);
        command.Parameters.AddWithValue(windowEnd);
        command.Parameters.AddWithValue(bucketSize);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var buckets = new List<MetricBucket>();

        while (await reader.ReadAsync(cancellationToken))
        {
            HistogramValue? histogram = reader.IsDBNull(13)
                ? null
                : new HistogramValue(
                    reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
                    reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    reader.IsDBNull(10) ? null : reader.GetDouble(10),
                    reader.IsDBNull(11) ? null : reader.GetDouble(11),
                    reader.IsDBNull(12) ? [] : reader.GetFieldValue<double[]>(12),
                    reader.GetFieldValue<long[]>(13));

            buckets.Add(new MetricBucket(
                SeriesId: reader.GetInt64(0),
                Start: reader.GetFieldValue<DateTimeOffset>(1),
                Points: (int)reader.GetInt64(2),
                Average: reader.IsDBNull(3) ? null : reader.GetDouble(3),
                Total: reader.IsDBNull(4) ? null : reader.GetDouble(4),
                LastTimestamp: reader.GetFieldValue<DateTimeOffset>(5),
                LastStart: reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                LastValue: reader.IsDBNull(7) ? null : reader.GetDouble(7),
                LastHistogram: histogram));
        }

        return buckets;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MetricName>> ListNamesAsync(
        string? search,
        DateTimeOffset seenSince,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT name, min(kind), min(unit), max(description), count(*),
                   (array_agg(DISTINCT coalesce(service_name, host_name))
                        FILTER (WHERE coalesce(service_name, host_name) IS NOT NULL))[1:5]
            FROM storage.metric_series
            WHERE last_seen >= $1 AND ($2::text IS NULL OR name ILIKE '%' || $2 || '%' ESCAPE '\')
            GROUP BY name
            ORDER BY name
            LIMIT 1000
            """);
        command.Parameters.AddWithValue(seenSince);
        command.Parameters.AddWithValue(
            NpgsqlDbType.Text,
            string.IsNullOrWhiteSpace(search) ? DBNull.Value : EscapeLike(search.Trim()));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var names = new List<MetricName>();

        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(new MetricName(
                reader.GetString(0),
                (MetricKind)reader.GetInt16(1),
                reader.GetString(2),
                reader.GetString(3),
                (int)reader.GetInt64(4),
                reader.IsDBNull(5) ? [] : reader.GetFieldValue<string[]>(5)));
        }

        return names;
    }

    /// <inheritdoc />
    public async Task<MetricStorageUsage> GetUsageAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            SELECT (SELECT count(*) FROM storage.metric_series),
                   pg_total_relation_size('storage.metric_series')
                   + (SELECT coalesce(sum(pg_total_relation_size(relid)), 0)
                      FROM pg_partition_tree('storage.metric_point'))
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var series = (int)reader.GetInt64(0);
        var bytes = (long)reader.GetDecimal(1);

        while (_written.TryPeek(out var oldest) && oldest.At < now - _rateWindow)
        {
            _written.TryDequeue(out _);
        }

        var recent = _written.Where(entry => entry.At <= now).Sum(entry => entry.Points);
        var settings = options.Value;

        return new MetricStorageUsage(
            series,
            settings.MaxMetricSeries,
            recent / _rateWindow.TotalSeconds,
            bytes,
            settings.MetricRetentionDays);
    }

    /// <summary>
    /// Forgets every cached series ID and the cached count. Retention calls it after deleting series, so the next
    /// point of such a series creates it again instead of pointing at a deleted ID.
    /// </summary>
    public void ClearSeriesCache()
    {
        _known.Clear();

        lock (_limitLock)
        {
            _seriesCount = null;
        }
    }

    private async Task ResolveSeriesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<MetricPoint> points,
        byte[][] hashes,
        string[] keys,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // The first point of each series not cached yet, in request order.
        var unknown = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < points.Count; index++)
        {
            if (!_known.ContainsKey(keys[index]))
            {
                unknown.TryAdd(keys[index], index);
            }
        }

        if (unknown.Count == 0)
        {
            return;
        }

        await using (var command = new NpgsqlCommand(
            "SELECT id, hash, last_seen FROM storage.metric_series WHERE hash = ANY($1)",
            connection))
        {
            command.Parameters.AddWithValue(unknown.Values.Select(index => hashes[index]).ToArray());

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var key = Convert.ToHexString(reader.GetFieldValue<byte[]>(1));
                _known[key] = new CachedSeries(reader.GetInt64(0), reader.GetFieldValue<DateTimeOffset>(2));
                unknown.Remove(key);
            }
        }

        foreach (var (key, index) in unknown)
        {
            if (!await TryReserveSeriesAsync(connection, cancellationToken))
            {
                continue;
            }

            var id = await InsertSeriesAsync(connection, points[index], hashes[index], now, cancellationToken);
            _known[key] = new CachedSeries(id, now);
        }
    }

    // Takes one place under the series limit; false when the limit is reached.
    private async Task<bool> TryReserveSeriesAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        int? count;

        lock (_limitLock)
        {
            count = _seriesCount;
        }

        if (count is null)
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM storage.metric_series", connection);
            count = (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        lock (_limitLock)
        {
            _seriesCount ??= count;

            if (_seriesCount >= options.Value.MaxMetricSeries)
            {
                return false;
            }

            _seriesCount++;

            return true;
        }
    }

    private static async Task<long> InsertSeriesAsync(
        NpgsqlConnection connection,
        MetricPoint point,
        byte[] hash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Another Veyra instance may have created it meanwhile; then its row is kept and its ID returned.
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO storage.metric_series
                (hash, name, kind, unit, description, temporality, monotonic, service_name, environment, host_name,
                 resource_attributes, attributes, first_seen, last_seen)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $13)
            ON CONFLICT (hash) DO UPDATE SET last_seen = greatest(storage.metric_series.last_seen, EXCLUDED.last_seen)
            RETURNING id
            """,
            connection);
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(point.Name);
        command.Parameters.AddWithValue((short)point.Kind);
        command.Parameters.AddWithValue(point.Unit);
        command.Parameters.AddWithValue(point.Description);
        command.Parameters.AddWithValue((short)point.Temporality);
        command.Parameters.AddWithValue(point.Monotonic);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)point.ServiceName ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)point.Environment ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)point.HostName ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(point.ResourceAttributes));
        command.Parameters.AddWithValue(NpgsqlDbType.Jsonb, JsonSerializer.Serialize(point.Attributes));
        command.Parameters.AddWithValue(now);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task RefreshLastSeenAsync(
        NpgsqlConnection connection,
        string[] keys,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var stale = new List<long>();

        foreach (var key in keys.Distinct(StringComparer.Ordinal))
        {
            if (_known.TryGetValue(key, out var series) && now - series.LastSeen >= _lastSeenRefresh)
            {
                stale.Add(series.Id);
                _known[key] = series with { LastSeen = now };
            }
        }

        if (stale.Count == 0)
        {
            return;
        }

        await using var command = new NpgsqlCommand(
            "UPDATE storage.metric_series SET last_seen = greatest(last_seen, $2) WHERE id = ANY($1)",
            connection);
        command.Parameters.AddWithValue(stale.ToArray());
        command.Parameters.AddWithValue(now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WritePointAsync(
        NpgsqlBinaryImporter writer,
        long seriesId,
        MetricPoint point,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(seriesId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(point.Timestamp, NpgsqlDbType.TimestampTz, cancellationToken);
        await WriteOrNullAsync(writer, point.StartTimestamp, NpgsqlDbType.TimestampTz, cancellationToken);
        await WriteOrNullAsync(writer, point.Value, NpgsqlDbType.Double, cancellationToken);

        var histogram = point.Histogram;
        await WriteOrNullAsync(writer, histogram?.Count, NpgsqlDbType.Bigint, cancellationToken);
        await WriteOrNullAsync(writer, histogram?.Sum, NpgsqlDbType.Double, cancellationToken);
        await WriteOrNullAsync(writer, histogram?.Min, NpgsqlDbType.Double, cancellationToken);
        await WriteOrNullAsync(writer, histogram?.Max, NpgsqlDbType.Double, cancellationToken);
        await WriteOrNullAsync(
            writer,
            histogram?.Bounds.ToArray(),
            NpgsqlDbType.Array | NpgsqlDbType.Double,
            cancellationToken);
        await WriteOrNullAsync(
            writer,
            histogram?.Counts.ToArray(),
            NpgsqlDbType.Array | NpgsqlDbType.Bigint,
            cancellationToken);
    }

    private static Task WriteOrNullAsync<T>(
        NpgsqlBinaryImporter writer,
        T? value,
        NpgsqlDbType type,
        CancellationToken cancellationToken) =>
        value is null ? writer.WriteNullAsync(cancellationToken) : writer.WriteAsync(value, type, cancellationToken);

    private void WarnAboutDropped(int dropped, SortedSet<string> names, DateTimeOffset now)
    {
        lock (_limitLock)
        {
            if (now - _lastDropWarning < _dropWarningInterval)
            {
                return;
            }

            _lastDropWarning = now;
        }

        LogDroppedAtLimit(dropped, options.Value.MaxMetricSeries, string.Join(", ", names.Take(5)));
    }

    private static void AppendEquals(StringBuilder sql, NpgsqlCommand command, string column, string? value)
    {
        if (value is null)
        {
            return;
        }

        command.Parameters.AddWithValue(value);
        sql.Append(CultureInfo.InvariantCulture, $" AND {column} = ${command.Parameters.Count}");
    }

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static Dictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Dropped {Count} metric points of new series: the limit of {Limit} series is reached. "
            + "Metrics: {Names}")]
    private partial void LogDroppedAtLimit(int count, int limit, string names);

    private sealed record CachedSeries(long Id, DateTimeOffset LastSeen);
}
