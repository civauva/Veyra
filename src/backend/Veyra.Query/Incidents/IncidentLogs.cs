using System.Text;
using Veyra.Core.Alerts;
using Veyra.Core.Telemetry;
using Veyra.Query.Incidents.Api;
using Veyra.Query.Logs;

namespace Veyra.Query.Incidents;

/// <summary>
/// Reads the warnings and worse of the affected blocks in an incident's window (ADR 0026), with the log query that
/// shows all of them on the Logs screen.
/// </summary>
/// <param name="logs">The stored logs.</param>
internal sealed class IncidentLogs(ILogSearch logs)
{
    /// <summary>How many records the incident's screen shows.</summary>
    public const int Shown = 50;

    /// <summary>Reads the records.</summary>
    /// <param name="blocks">The affected blocks; services, hosts, and containers write logs.</param>
    /// <param name="environment">The incident's environment.</param>
    /// <param name="window">The incident's window.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>The newest records, the query, and how many match.</returns>
    public async Task<IncidentLogsResponse> ReadAsync(
        IReadOnlyList<AlertSubject> blocks,
        string? environment,
        IncidentWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(window);

        // A record's service is its block's name, whatever the kind (ADR 0018), so one filter covers them all.
        var names = blocks
            .Where(block => block.Kind != AlertBlockKind.Database)
            .Select(block => block.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (names.Count == 0)
        {
            return new IncidentLogsResponse([], string.Empty, null);
        }

        var query = Query(names, environment, window);
        var page = await logs.SearchAsync(query, cancellationToken);
        var span = window.To - window.From;
        var bucket = TimeSpan.FromMinutes(Math.Max(1, Math.Ceiling(span.TotalMinutes)));
        var summary = await logs.SummarizeAsync(query, bucket, cancellationToken);

        return new IncidentLogsResponse(
            [.. page.Items.Select(LogsController.ToEntry)],
            QueryText(names, environment),
            summary.Buckets.Sum(one => one.Total));
    }

    /// <summary>
    /// The log query (ADR 0024) for the warnings and worse of some blocks: each block as a <c>service:</c> term, which
    /// repeated means any of them, then the environment and the level.
    /// </summary>
    /// <param name="names">The blocks' names.</param>
    /// <param name="environment">The environment, or <see langword="null"/> for every one.</param>
    /// <returns>The query, such as <c>service:checkout AND level&gt;=warning</c>.</returns>
    public static string QueryText(IReadOnlyList<string> names, string? environment)
    {
        ArgumentNullException.ThrowIfNull(names);

        var terms = names.Select(name => "service:" + Value(name)).ToList();

        if (environment is not null)
        {
            terms.Add("env:" + Value(environment));
        }

        terms.Add("level>=warning");

        return string.Join(" AND ", terms);
    }

    /// <summary>The storage query for the warnings and worse of some blocks over a window, newest first.</summary>
    /// <param name="names">The blocks' names.</param>
    /// <param name="environment">The environment, or <see langword="null"/> for every one.</param>
    /// <param name="window">The window.</param>
    /// <returns>The query.</returns>
    public static LogQuery Query(IReadOnlyList<string> names, string? environment, IncidentWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        return new LogQuery(
            From: window.From,
            To: window.To,
            MinimumSeverity: LogSeverity.Warning,
            Services: names,
            Text: null,
            TraceId: null,
            Limit: Shown,
            After: null,
            Environments: environment is null ? null : [environment]);
    }

    // A value is quoted when a space, a quote, or nothing would end it early; inside quotes \ and " are escaped.
    private static string Value(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c == '"'))
        {
            return value;
        }

        var quoted = new StringBuilder("\"");

        foreach (var c in value)
        {
            if (c is '"' or '\\')
            {
                quoted.Append('\\');
            }

            quoted.Append(c);
        }

        return quoted.Append('"').ToString();
    }
}
