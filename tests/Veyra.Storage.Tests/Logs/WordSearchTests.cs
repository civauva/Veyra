using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Veyra.Core.Schema;
using Veyra.Core.Telemetry;
using Veyra.Storage.Logs;
using Veyra.Storage.Schema;
using Veyra.Testing;

namespace Veyra.Storage.Tests.Logs;

/// <summary>
/// Words are indexed with the hour they were written, and a search walks the hours newest first (ADR 0016), so a word
/// that stopped appearing is found as fast as one that is everywhere. These tests prove the walk returns exactly what
/// the records hold, and that an upgraded installation searches correctly before, during, and after its indexes are
/// built.
/// </summary>
[Collection(PostgresGroup.Name)]
public class WordSearchTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _day = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SearchAsync_WordLastSeenHoursEarlier_ReturnsItsNewestRecordsPageByPage()
    {
        await using var database = await CreateAsync(_day);
        var store = new LogStore(database);
        var declined = Enumerable.Range(0, 30)
            .Select(minute => Log(_day.AddHours(2).AddMinutes(minute), $"card declined {minute}"));
        var accepted = Enumerable.Range(0, 6 * 60)
            .Select(minute => Log(_day.AddHours(3).AddMinutes(minute), "payment accepted"));
        await store.WriteAsync([.. declined, .. accepted], default);

        var pages = await AllPagesAsync(store, Query("declined", _day, _day.AddHours(10), limit: 12));

        Assert.Equal([12, 12, 6], pages.Select(page => page.Count));
        Assert.Equal(
            Enumerable.Range(0, 30).Reverse().Select(minute => $"card declined {minute}"),
            pages.SelectMany(page => page));
    }

    [Fact]
    public async Task SearchAsync_TwoWords_FindsOnlyRecordsWithBoth()
    {
        await using var database = await CreateAsync(_day);
        var store = new LogStore(database);
        await store.WriteAsync(
            [
                Log(_day.AddHours(1), "card declined"),
                Log(_day.AddHours(2), "card accepted"),
                Log(_day.AddHours(3), "payment declined"),
            ],
            default);

        var page = await store.SearchAsync(Query("Card DECLINED", _day, _day.AddHours(4)), default);

        Assert.Equal(["card declined"], page.Items.Select(item => item.Record.Body));
    }

    [Fact]
    public async Task SearchAsync_TextWithoutWords_FindsNothing()
    {
        await using var database = await CreateAsync(_day);
        var store = new LogStore(database);
        await store.WriteAsync([Log(_day.AddHours(1), "card declined"), Log(_day.AddHours(2), "")], default);

        var query = Query("!!!", _day, _day.AddHours(4));

        Assert.Empty((await store.SearchAsync(query, default)).Items);
        Assert.Equal(0, (await store.SummarizeAsync(query, TimeSpan.FromHours(1), default)).Buckets.Sum(b => b.Total));
    }

    [Fact]
    public async Task SearchAsync_RecordInTheLastMomentOfAnHour_IsFoundAndCounted()
    {
        await using var database = await CreateAsync(_day);
        var store = new LogStore(database);
        var lastMoment = _day.AddHours(3).AddTicks(-1000);
        await store.WriteAsync([Log(lastMoment, "card declined")], default);

        var query = Query("declined", _day.AddHours(2).AddMinutes(30), _day.AddHours(3).AddMinutes(30));
        var page = await store.SearchAsync(query, default);
        var summary = await store.SummarizeAsync(query, TimeSpan.FromMinutes(30), default);

        Assert.Equal(lastMoment, Assert.Single(page.Items).Record.Timestamp);
        Assert.Equal(1, summary.Buckets.Sum(bucket => bucket.Total));
    }

    [Fact]
    public async Task CreateWordSearchCommand_BusyDay_ReadsTheWordsByHourIndex()
    {
        await using var database = await CreateBusyDayAsync();
        var query = Query("declined", _day, _day.AddDays(1));

        await using var command = LogStore.CreateWordSearchCommand(query, query.From, query.To, rows: 101);
        var plan = await QueryPlan.ExplainAsync(
            database,
            command,
            [("enable_indexscan", "off"), ("enable_seqscan", "off")]);

        Assert.Contains(plan.Indexes, index => index.Contains("log_terms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateCommand_WordCountsOverABusyDay_ReadTheWordsByHourIndex()
    {
        await using var database = await CreateBusyDayAsync();
        var query = Query("declined", _day, _day.AddDays(1));

        await using var command = LogCounts.CreateCommand(
            query,
            TimeSpan.FromHours(1),
            countable: false,
            uncounted: [],
            lexemes: ["declined"],
            unindexed: []);
        var plan = await QueryPlan.ExplainAsync(database, command, [("enable_seqscan", "off")]);

        Assert.Contains(plan.Indexes, index => index.Contains("log_terms", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildNextDayAsync_DaysWrittenBeforeTheUpgrade_IndexesThemNewestFirstAndDropsTheOldIndex()
    {
        // As an installation upgrading from ADR 0015: two days of records under the old word index.
        await using var database = await postgres.CreateMigratedDatabaseAsync(new MigrationsUpTo(4));
        var partitions = new LogPartitions(database, TimeProvider.System, NullLogger<LogPartitions>.Instance);
        await partitions.EnsureAsync(_day, default);
        await WriteAsBeforeAsync(database, _day.AddDays(-1).AddHours(5), "card declined");
        await WriteAsBeforeAsync(database, _day.AddHours(5), "card declined");

        await new SchemaMigrator(
            database,
            [new StorageMigrations()],
            TimeProvider.System,
            NullLogger<SchemaMigrator>.Instance).MigrateAsync(default);
        var store = new LogStore(database);
        var query = Query("declined", _day.AddDays(-1), _day.AddDays(1));
        var backfill = new LogWordIndexBackfill(database);

        // Before the indexes are built, the days are searched and counted the old way.
        Assert.Equal(2, (await store.SearchAsync(query, default)).Items.Count);
        Assert.Equal(2, (await store.SummarizeAsync(query, TimeSpan.FromHours(1), default)).Buckets.Sum(b => b.Total));

        // A day created after the upgrade gets the new index straight away.
        await partitions.EnsureAsync(_day.AddDays(3), default);
        Assert.Equal(1L, await ScalarAsync(database, TermsIndexCount("log_record_20261114")));

        // The days around the upgrade were 8 to 11 November; each step builds the newest one left.
        var newestLeft = new List<object?>();

        while (await backfill.BuildNextDayAsync(default))
        {
            newestLeft.Add(await ScalarAsync(database, "SELECT max(day)::text FROM storage.log_terms_backfill"));
        }

        Assert.Equal(["2026-11-10", "2026-11-09", "2026-11-08", null], newestLeft);
        Assert.Equal(1L, await ScalarAsync(database, TermsIndexCount("log_record_20261108")));
        Assert.Null(await ScalarAsync(database, "SELECT to_regclass('storage.log_record_body_text')::text"));
        Assert.Equal(2, (await store.SearchAsync(query, default)).Items.Count);
        Assert.Equal(2, (await store.SummarizeAsync(query, TimeSpan.FromHours(1), default)).Buckets.Sum(b => b.Total));
    }

    /// <summary>
    /// A day of 120,000 analysed records with a declined card among every 500: busy enough that reading a whole hour
    /// costs more than looking a word up, as on a real installation.
    /// </summary>
    private async Task<NpgsqlDataSource> CreateBusyDayAsync()
    {
        var database = await CreateAsync(_day);

        await using var fill = database.CreateCommand(
            """
            INSERT INTO storage.log_record
                (timestamp, observed_at, severity, severity_text, body, service_name, environment, attributes,
                 resource_attributes)
            SELECT at, at, 3, 'Information',
                   CASE WHEN i % 500 = 0 THEN 'card declined' ELSE 'payment accepted ' || i END,
                   'payments', 'production', '{}', '{}'
            FROM generate_series(0, 119999) AS i,
                 LATERAL (SELECT timestamptz '2026-11-09 00:00:00+00' + i * interval '720 ms') AS time (at);
            ANALYZE storage.log_record;
            """);
        await fill.ExecuteNonQueryAsync();

        return database;
    }

    private static string TermsIndexCount(string partition) =>
        $"""
        SELECT count(*) FROM pg_indexes
        WHERE schemaname = 'storage' AND tablename = '{partition}' AND indexdef LIKE '%log_terms%'
        """;

    private static LogQuery Query(string text, DateTimeOffset from, DateTimeOffset to, int limit = 100) =>
        new(from, to, null, null, text, null, limit, null);

    private static async Task<List<List<string>>> AllPagesAsync(LogStore store, LogQuery query)
    {
        var pages = new List<List<string>>();
        var page = await store.SearchAsync(query, default);
        pages.Add(page.Items.Select(item => item.Record.Body).ToList());

        while (page.Next is { } next)
        {
            page = await store.SearchAsync(query with { After = next }, default);
            pages.Add(page.Items.Select(item => item.Record.Body).ToList());
        }

        return pages;
    }

    private static LogRecord Log(DateTimeOffset timestamp, string body) => new(
        Timestamp: timestamp,
        ObservedTimestamp: timestamp,
        Severity: LogSeverity.Information,
        SeverityText: "Information",
        Body: body,
        ServiceName: "payments",
        Environment: "production",
        TraceId: null,
        SpanId: null,
        Attributes: new Dictionary<string, string>(),
        ResourceAttributes: new Dictionary<string, string>());

    /// <summary>
    /// Writes one record and its counts as Veyra did before the word index by hour, with the columns of that schema
    /// only: today's writer knows columns that came later.
    /// </summary>
    private static async Task WriteAsBeforeAsync(NpgsqlDataSource database, DateTimeOffset timestamp, string body)
    {
        await using var command = database.CreateCommand(
            """
            WITH record AS (
                INSERT INTO storage.log_record
                    (timestamp, observed_at, severity, severity_text, body, service_name, environment, attributes,
                     resource_attributes)
                VALUES ($1, $1, $2, 'Information', $3, 'payments', 'production', '{}', '{}')),
            minute AS (
                INSERT INTO storage.log_count_minute (minute, environment, service_name, severity, records)
                VALUES (date_trunc('minute', $1), 'production', 'payments', $2, 1))
            INSERT INTO storage.log_count_hour (hour, environment, service_name, severity, records)
            VALUES (date_trunc('hour', $1), 'production', 'payments', $2, 1)
            """);
        command.Parameters.AddWithValue(timestamp);
        command.Parameters.AddWithValue((short)LogSeverity.Information);
        command.Parameters.AddWithValue(body);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A migrated database with daily partitions from the first day to the last, analysed.</summary>
    private async Task<NpgsqlDataSource> CreateAsync(DateTimeOffset first, DateTimeOffset? last = null)
    {
        var database = await postgres.CreateMigratedDatabaseAsync(new StorageMigrations());
        var partitions = new LogPartitions(database, TimeProvider.System, NullLogger<LogPartitions>.Instance);

        for (var day = first; day <= (last ?? first); day = day.AddDays(1))
        {
            await partitions.EnsureAsync(day, default);
        }

        return database;
    }

    private static async Task<object?> ScalarAsync(NpgsqlDataSource database, string sql)
    {
        await using var command = database.CreateCommand(sql);
        var value = await command.ExecuteScalarAsync();

        return value is DBNull ? null : value;
    }

    /// <summary>The storage migrations up to a version, as an installation of an older release has them.</summary>
    private sealed class MigrationsUpTo(int version) : ISchemaMigrationSource
    {
        public IReadOnlyList<SchemaMigration> GetMigrations() =>
            new StorageMigrations().GetMigrations().Where(migration => migration.Version <= version).ToList();
    }
}
