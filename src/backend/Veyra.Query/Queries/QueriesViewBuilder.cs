using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Metrics;
using Veyra.Core.Telemetry;
using Veyra.Query.Live;
using Veyra.Query.Metrics;
using Veyra.Query.Queries.Api;
using Veyra.Query.Systems;

namespace Veyra.Query.Queries;

/// <summary>
/// Builds a SQL Server's Queries tab and each statement's page (ADR 0020, area 4) from the statement series the agent
/// sends every minute, split by plan, and the plans it captures every 15 minutes.
/// </summary>
/// <param name="search">The metric series and points.</param>
/// <param name="activity">The captured plans and the live view's reads.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class QueriesViewBuilder(IMetricSearch search, IDatabaseActivitySearch activity, TimeProvider clock)
{
    /// <summary>The kind of record a captured plan is.</summary>
    public const string PlanKind = "sqlserver.plan";

    /// <summary>The kind of record a Query Store row is.</summary>
    public const string QueryStoreKind = "sqlserver.querystore";

    private const int _maxQueryStoreRows = 20_000;

    private const int _maxSeries = 8_000;
    private const int _maxStatements = 100;
    private const int _maxPlans = 200;

    // A plan is a regression when a statement runs this many times slower on it than on its best plan.
    private const double _regressionFactor = 2;
    private const long _regressionRuns = 5;

    /// <summary>Builds the Queries tab.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server's name.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="listing">Whether the server's own statements are listed, and what they are ranked by.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The tab.</returns>
    public async Task<QueriesResponse> ListAsync(
        string engine,
        string server,
        MetricSpan span,
        QueryListing listing,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(span, clock.GetUtcNow());
        var block = TelemetryBlock.Database(engine, server);
        var (series, totals) = await TotalsAsync(block, window, cancellationToken);
        ArgumentNullException.ThrowIfNull(listing);

        var statements = Statements(series, totals);
        var measure = Measure(listing.Sort);
        // The server's own statements are left out unless asked for, and listed apart when they are, so its
        // housekeeping never crowds out the applications' work. Both happen before the cut to 100 (VEY-66).
        var (listed, hidden) = SystemRows.Keep(
            statements.Where(statement => !statement.Agent),
            listing.System,
            statement => SystemObjects.IsSystemStatement(statement.Database, statement.Text),
            statement => SystemRows.SystemDatabase(SqlServerNames.Engine, statement.Database));
        var (rows, busiest) = listing.Simple
            ? await WorthALookAsync(block, window, listed, measure, cancellationToken)
            : await RankedAsync(block, window, listed, measure, cancellationToken);
        var agent = statements.Where(statement => statement.Agent).ToList();
        var now = clock.GetUtcNow();
        var states = (await activity.ListRecordsAsync(
                block.Name,
                QueryStoreKind,
                now.AddDays(-2),
                now,
                null,
                _maxQueryStoreRows,
                cancellationToken))
            .Where(record => record.Key.EndsWith("|state", StringComparison.Ordinal))
            .GroupBy(record => record.Attributes.GetValueOrDefault("database_name") ?? "")
            .Select(group => group.First())
            .Select(record => QueryStoreState(
                record.Attributes.GetValueOrDefault("database_name") ?? "",
                record.Body == "on"))
            .OrderBy(state => state.On)
            .ThenBy(state => state.Database, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new QueriesResponse(
            window.Range,
            rows,
            agent.Count,
            agent.Sum(statement => statement.Seconds),
            states)
        {
            From = window.From,
            To = window.End,
            HiddenStatements = hidden,
            Busiest = busiest,
        };
    }

    // The Advanced view: the top 100 by the measure asked for, the server's own apart, so its housekeeping never
    // crowds out the applications' work.
    private async Task<(List<QueryRowResponse> Rows, QueryRowResponse? Busiest)> RankedAsync(
        TelemetryBlock block,
        MetricWindow window,
        IReadOnlyList<Statement> listed,
        Func<Statement, double> measure,
        CancellationToken cancellationToken)
    {
        var mine = listed
            .GroupBy(statement => SystemObjects.IsSystemStatement(statement.Database, statement.Text))
            .SelectMany(group => group.OrderByDescending(measure).Take(_maxStatements))
            .OrderByDescending(measure)
            .ThenByDescending(statement => statement.Seconds)
            .ToList();
        var plans = await PlansAsync(
            block,
            window,
            mine.SelectMany(statement => statement.Plans.Select(plan => plan.Hash)).ToList(),
            cancellationToken);
        var rows = mine.Select(statement => Row(statement, plans)).ToList();
        var busiest = listed.MaxBy(statement => statement.Seconds);

        return (rows, busiest is null ? null : rows.Find(row => row.QueryHash == busiest.Hash) ?? Row(busiest, plans));
    }

    // The Simple view: the statements with a finding, by the measure, at most 100. Each statement's newest plan is
    // read before the cut, since a finding can be on a statement outside the busiest (VEY-67).
    private async Task<(List<QueryRowResponse> Rows, QueryRowResponse? Busiest)> WorthALookAsync(
        TelemetryBlock block,
        MetricWindow window,
        IReadOnlyList<Statement> listed,
        Func<Statement, double> measure,
        CancellationToken cancellationToken)
    {
        var ranked = listed.OrderByDescending(measure).ThenByDescending(statement => statement.Seconds).ToList();
        var plans = await PlansAsync(
            block,
            window,
            [.. ranked.Select(statement => statement.Plans.MaxBy(plan => plan.LastSeen)?.Hash).OfType<string>()],
            cancellationToken);
        var rows = ranked.Select(statement => Row(statement, plans)).ToList();
        var busiest = listed.MaxBy(statement => statement.Seconds);

        return (
            [.. rows.Where(row => row.Findings.Count > 0).Take(_maxStatements)],
            busiest is null ? null : rows.Find(row => row.QueryHash == busiest.Hash));
    }

    // The figure a statement is ranked by.
    private static Func<Statement, double> Measure(QuerySort sort) => sort switch
    {
        QuerySort.Cpu => statement => statement.Cpu,
        QuerySort.Reads => statement => statement.Reads,
        QuerySort.Runs => statement => statement.Runs,
        QuerySort.TimePerRun => statement => statement.Runs > 0 ? statement.Seconds / statement.Runs : 0,
        _ => statement => statement.Seconds,
    };

    /// <summary>Builds a statement's page.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server's name.</param>
    /// <param name="queryHash">The statement's query hash.</param>
    /// <param name="span">The window: a named range ending now, or a from–to period (ADR 0022).</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The page, or <see langword="null"/> when the statement did not run in the range.</returns>
    public async Task<QueryResponse?> QueryAsync(
        string engine,
        string server,
        string queryHash,
        MetricSpan span,
        CancellationToken cancellationToken)
    {
        var window = MetricWindow.Of(span, clock.GetUtcNow());
        var block = TelemetryBlock.Database(engine, server);
        var (all, totals) = await TotalsAsync(block, window, cancellationToken);
        var statement = Statements(all, totals).FirstOrDefault(one => one.Hash == queryHash);

        if (statement is null)
        {
            return null;
        }

        var ids = statement.Series.Select(series => series.Id).ToList();
        var bucketsReading = search.ReadBucketsAsync(ids, window.Start, window.End, window.Bucket, cancellationToken);
        var plansReading = PlansAsync(
            block,
            window,
            statement.Plans.Select(plan => plan.Hash).ToList(),
            cancellationToken);
        var callersReading = activity.ListCallersAsync(
            block.Name,
            queryHash,
            window.Start,
            window.End,
            cancellationToken);
        var storeReading = activity.ListRecordsAsync(
            block.Name,
            QueryStoreKind,
            window.Start,
            window.End.AddMinutes(20),
            null,
            _maxQueryStoreRows,
            cancellationToken);

        await Task.WhenAll(bucketsReading, plansReading, callersReading, storeReading);

        var history = History(await storeReading, queryHash);

        var plans = await plansReading;
        var byId = statement.Series.ToDictionary(series => series.Id);
        var points = (await bucketsReading)
            .GroupBy(bucket => bucket.Start)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                double Sum(string name, string? plan = null) => group
                    .Where(bucket => byId[bucket.SeriesId].Name == name
                        && (plan is null || Plan(byId[bucket.SeriesId]) == plan))
                    .Sum(bucket => bucket.Total ?? 0);

                var runs = (long)Sum(SqlServerNames.StatementExecutions);
                var main = group
                    .Where(bucket => byId[bucket.SeriesId].Name == SqlServerNames.StatementExecutions)
                    .OrderByDescending(bucket => bucket.Total ?? 0)
                    .Select(bucket => Plan(byId[bucket.SeriesId]))
                    .FirstOrDefault(plan => plan.Length > 0);

                return new QueryPointResponse(
                    group.Key,
                    runs,
                    runs > 0 ? Sum(SqlServerNames.StatementElapsed) / runs : null,
                    runs > 0 ? Sum(SqlServerNames.StatementCpu) / runs : null,
                    runs > 0 ? Sum(SqlServerNames.StatementReads) / runs : null,
                    main);
            })
            .ToList();
        var planResponses = statement.Plans
            .OrderByDescending(plan => plan.LastSeen)
            .Select(plan =>
            {
                var (operators, warnings) = plans.TryGetValue(plan.Hash, out var xml)
                    ? PlanReader.Read(xml)
                    : ([], []);

                return new QueryPlanResponse(
                    plan.Hash,
                    plan.Runs,
                    plan.PerRun,
                    plan.Runs > 0 ? plan.Cpu / plan.Runs : 0,
                    plan.Runs > 0 ? plan.Reads / plan.Runs : 0,
                    plan.FirstSeen,
                    plan.LastSeen,
                    plans.ContainsKey(plan.Hash),
                    operators,
                    warnings);
            })
            .ToList();
        var row = Row(statement, plans);

        return new QueryResponse(
            statement.Hash,
            statement.Database,
            StatementNames.Describe(statement.Text),
            statement.Text,
            window.Range,
            row,
            points,
            planResponses,
            row.Findings,
            await callersReading,
            history,
            history
                .Where(interval => interval.TopWait.Length > 0)
                .GroupBy(interval => interval.TopWait)
                .Select(group => new QueryWaitResponse(group.Key, group.Sum(interval => interval.WaitSeconds)))
                .OrderByDescending(wait => wait.Seconds)
                .ToList())
        {
            From = window.From,
            To = window.End,
        };
    }

    /// <summary>Reads a captured plan's XML, for the <c>.sqlplan</c> download.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server's name.</param>
    /// <param name="planHash">The plan hash.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The XML, or <see langword="null"/> when Veyra holds none.</returns>
    public async Task<string?> PlanXmlAsync(
        string engine,
        string server,
        string planHash,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var records = await activity.ListRecordsAsync(
            TelemetryBlock.Database(engine, server).Name,
            PlanKind,
            now.AddDays(-90),
            now,
            [planHash],
            1,
            cancellationToken);

        return records.Count == 0 || records[0].Body.Length == 0 ? null : records[0].Body;
    }

    /// <summary>
    /// Groups statement series into statements with their plans and totals. Pure; tested without a database.
    /// </summary>
    /// <param name="series">The statement series.</param>
    /// <param name="totals">Each series' total over the range.</param>
    /// <returns>The statements, by total time.</returns>
    public static List<Statement> Statements(
        IReadOnlyList<MetricSeries> series,
        IReadOnlyDictionary<long, double> totals)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(totals);

        return series
            .GroupBy(one => (
                Hash: MetricNames.Attribute(one, "query_hash") ?? "",
                Database: MetricNames.Attribute(one, "database_name") ?? ""))
            .Where(group => group.Key.Hash.Length > 0)
            .Select(group =>
            {
                var plans = group
                    .GroupBy(Plan)
                    .Where(plan => plan.Key.Length > 0)
                    .Select(plan =>
                    {
                        double PlanSum(string name) => plan
                            .Where(one => one.Name == name)
                            .Sum(one => totals.GetValueOrDefault(one.Id));
                        var runs = (long)PlanSum(SqlServerNames.StatementExecutions);

                        return new StatementPlan(
                            plan.Key,
                            runs,
                            runs > 0 ? PlanSum(SqlServerNames.StatementElapsed) / runs : 0,
                            PlanSum(SqlServerNames.StatementCpu),
                            PlanSum(SqlServerNames.StatementReads),
                            plan.Min(one => one.FirstSeen),
                            plan.Max(one => one.LastSeen));
                    })
                    .ToList();

                double Sum(string name) => group
                    .Where(one => one.Name == name)
                    .Sum(one => totals.GetValueOrDefault(one.Id));
                var text = group
                    .Select(one => MetricNames.Attribute(one, "query_text") ?? "")
                    .MaxBy(one => one.Length) ?? "";

                return new Statement(
                    group.Key.Hash,
                    group.Key.Database.Length > 0 ? group.Key.Database : null,
                    text,
                    group.Any(one => MetricNames.Attribute(one, "agent") == "true"),
                    Sum(SqlServerNames.StatementElapsed),
                    Sum(SqlServerNames.StatementCpu),
                    (long)Sum(SqlServerNames.StatementExecutions),
                    (long)Sum(SqlServerNames.StatementReads),
                    plans,
                    group.ToList());
            })
            .Where(statement => statement.Seconds > 0 || statement.Runs > 0)
            .OrderByDescending(statement => statement.Seconds)
            .ToList();
    }

    /// <summary>
    /// What a statement's plans and history show (ADR 0020): a plan regression, and each captured plan's warnings.
    /// Pure; tested without a database.
    /// </summary>
    /// <param name="statement">The statement.</param>
    /// <param name="plans">The captured plans' XML, by plan hash.</param>
    /// <returns>The row, with its findings the worst first.</returns>
    public static QueryRowResponse Row(Statement statement, IReadOnlyDictionary<string, string> plans)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(plans);

        var findings = new List<PlanWarningResponse>();
        var latest = statement.Plans.MaxBy(plan => plan.LastSeen);
        var best = statement.Plans.Where(plan => plan.Runs >= _regressionRuns).MinBy(plan => plan.PerRun);

        if (latest is not null && best is not null && latest != best && latest.Runs >= _regressionRuns
            && latest.PerRun >= best.PerRun * _regressionFactor && latest.PerRun > 0.01)
        {
            findings.Add(new PlanWarningResponse(
                "regression",
                "warning",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{latest.PerRun / best.PerRun:0.#} times slower on its newest plan"),
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Each run takes {Seconds(latest.PerRun)} on plan {latest.Hash}, against "
                    + $"{Seconds(best.PerRun)} on plan "
                    + $"{best.Hash}. SQL Server chose a worse way to run it."),
                "Fix what made the plan worse (see the plan\x27s warnings), or a DBA can make SQL Server use the "
                + "old plan with Query Store.",
                null));
        }

        if (latest is not null && plans.TryGetValue(latest.Hash, out var xml))
        {
            findings.AddRange(PlanReader.Read(xml).Warnings);
        }

        return new QueryRowResponse(
            statement.Hash,
            statement.Database,
            StatementNames.Describe(statement.Text),
            statement.Text,
            statement.Seconds,
            statement.Runs,
            statement.Cpu,
            statement.Reads,
            statement.Runs > 0 ? statement.Seconds / statement.Runs : 0,
            statement.Plans.Count,
            findings.OrderBy(finding => finding.Severity == "warning" ? 0 : 1).ToList(),
            SystemObjects.IsSystemStatement(statement.Database, statement.Text));
    }

    private async Task<(IReadOnlyList<MetricSeries> Series, Dictionary<long, double> Totals)> TotalsAsync(
        TelemetryBlock block,
        MetricWindow window,
        CancellationToken cancellationToken)
    {
        var series = await search.FindSeriesAsync(
            new MetricSeriesQuery(window.Start, Names: SqlServerNames.Statements, Limit: _maxSeries, Block: block),
            cancellationToken);

        if (series.Count == 0)
        {
            return (series, []);
        }

        var buckets = await search.ReadBucketsAsync(
            series.Select(one => one.Id).ToList(),
            window.Start,
            window.End,
            window.End - window.Start,
            cancellationToken);

        return (
            series,
            buckets
                .GroupBy(bucket => bucket.SeriesId)
                .ToDictionary(group => group.Key, group => group.Sum(b => b.Total ?? 0)));
    }

    private async Task<Dictionary<string, string>> PlansAsync(
        TelemetryBlock block,
        MetricWindow window,
        List<string> hashes,
        CancellationToken cancellationToken)
    {
        if (hashes.Count == 0)
        {
            return [];
        }

        var records = await activity.ListRecordsAsync(
            block.Name,
            PlanKind,
            window.Start.AddDays(-1),
            window.End,
            hashes,
            _maxPlans,
            cancellationToken);

        return records
            .Where(record => record.Body.Length > 0)
            .GroupBy(record => record.Key)
            .ToDictionary(group => group.Key, group => group.First().Body);
    }

    /// <summary>
    /// How many times slower a statement runs on its newest plan than on its best one; 0 when it has one plan or too
    /// few runs to judge.
    /// </summary>
    /// <param name="statement">The statement.</param>
    /// <returns>The factor.</returns>
    public static double RegressionFactor(Statement statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var latest = statement.Plans.MaxBy(plan => plan.LastSeen);
        var best = statement.Plans.Where(plan => plan.Runs >= _regressionRuns).MinBy(plan => plan.PerRun);

        return latest is null || best is null || latest == best || latest.Runs < _regressionRuns || best.PerRun <= 0
            ? 0
            : latest.PerRun / best.PerRun;
    }

    /// <summary>
    /// A statement's Query Store intervals from the rows the agent sent. Pure; tested without a database.
    /// </summary>
    /// <param name="records">The Query Store rows.</param>
    /// <param name="queryHash">The statement's query hash.</param>
    /// <returns>Its intervals, oldest first, each plan once.</returns>
    public static IReadOnlyList<QueryStoreIntervalResponse> History(
        IReadOnlyList<ActivityRecord> records,
        string queryHash)
    {
        ArgumentNullException.ThrowIfNull(records);

        static double Number(ActivityRecord record, string key) =>
            double.TryParse(
                record.Attributes.GetValueOrDefault(key),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0;

        return records
            .Where(record => record.Attributes.GetValueOrDefault("query_hash") == queryHash)
            .GroupBy(record => record.Key)
            .Select(group => group.First())
            .Select(record =>
            {
                var runs = (long)Number(record, "executions");

                return new QueryStoreIntervalResponse(
                    DateTimeOffset.FromUnixTimeSeconds((long)Number(record, "interval_start")),
                    DateTimeOffset.FromUnixTimeSeconds((long)Number(record, "interval_end")),
                    record.Attributes.GetValueOrDefault("plan_hash") ?? "",
                    runs,
                    runs > 0 ? Number(record, "elapsed_seconds") / runs : 0,
                    runs > 0 ? Number(record, "cpu_seconds") / runs : 0,
                    runs > 0 ? Number(record, "logical_reads") / runs : 0,
                    record.Attributes.GetValueOrDefault("top_wait") ?? "",
                    Number(record, "wait_seconds"));
            })
            .OrderBy(interval => interval.Start)
            .ToList();
    }

    /// <summary>A database's Query Store state, with the statement to turn it on.</summary>
    /// <param name="database">The database.</param>
    /// <param name="on">Whether Query Store is on.</param>
    /// <returns>The state.</returns>
    public static QueryStoreStateResponse QueryStoreState(string database, bool on) =>
        new(
            database,
            on,
            on
                ? null
                : $"ALTER DATABASE [{database.Replace("]", "]]", StringComparison.Ordinal)}] SET QUERY_STORE = ON "
                + "(OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = AUTO);");

    private static string Plan(MetricSeries series) => MetricNames.Attribute(series, "plan_hash") ?? "";

    private static string Seconds(double seconds) =>
        seconds < 1
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds * 1000:0} ms")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds:0.##} s");
}

/// <summary>A statement over the range, with its plans and series.</summary>
/// <param name="Hash">Its query hash.</param>
/// <param name="Database">Its database, or <see langword="null"/>.</param>
/// <param name="Text">Its text.</param>
/// <param name="Agent">Whether it is Veyra's agent's own.</param>
/// <param name="Seconds">Its time over the range.</param>
/// <param name="Cpu">Its CPU over the range.</param>
/// <param name="Runs">Its runs over the range.</param>
/// <param name="Reads">Its reads over the range.</param>
/// <param name="Plans">Its plans over the range.</param>
/// <param name="Series">Its series.</param>
internal sealed record Statement(
    string Hash,
    string? Database,
    string Text,
    bool Agent,
    double Seconds,
    double Cpu,
    long Runs,
    long Reads,
    IReadOnlyList<StatementPlan> Plans,
    IReadOnlyList<MetricSeries> Series);

/// <summary>One plan of a statement over the range.</summary>
/// <param name="Hash">The plan hash.</param>
/// <param name="Runs">Runs on it.</param>
/// <param name="PerRun">Seconds per run on it.</param>
/// <param name="Cpu">CPU seconds on it.</param>
/// <param name="Reads">Reads on it.</param>
/// <param name="FirstSeen">When first reported.</param>
/// <param name="LastSeen">When last reported.</param>
internal sealed record StatementPlan(
    string Hash,
    long Runs,
    double PerRun,
    double Cpu,
    double Reads,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen);
