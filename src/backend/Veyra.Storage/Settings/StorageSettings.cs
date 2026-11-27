using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Veyra.Core;
using Veyra.Core.Settings;
using Veyra.Storage.Activity;
using Veyra.Storage.Jobs;
using Veyra.Storage.Metrics;
using Veyra.Storage.Traces;

namespace Veyra.Storage.Settings;

/// <summary>
/// The Storage module's settings on the Settings screen: how long logs, metrics, and traces are kept and how much room
/// they may take, and what a change deletes (ADR 0017).
/// </summary>
/// <param name="footprint">Measures stored data; null when storage is not configured.</param>
/// <param name="rules">The rules <see cref="StorageOptions"/> must meet.</param>
internal sealed class StorageSettings(
    StorageFootprint? footprint,
    IEnumerable<IValidateOptions<StorageOptions>> rules) : IModuleSettings
{
    private const string _prefix = VeyraOptionNames.Storage + ":";

    private const double _bytesPerGigabyte = 1024d * 1024 * 1024;

    // Each kind of data: its name in sentences, the tables of a day, and its retention and budget settings.
    private static readonly Signal[] _signals =
    [
        new("logs", ["log_record", "log_count_minute", "log_count_hour"], "LogRetentionDays", "MaxLogStorageGigabytes"),
        new("metrics", MetricPartitions.Tables, "MetricRetentionDays", null),
        new("traces", [SpanStore.Table], "TraceRetentionDays", "MaxTraceStorageGigabytes"),
        new("live sessions", [ActivityTables.Sessions], "DatabaseSessionRetentionDays", null),
        new("database records", [ActivityTables.Records], "DatabaseRecordRetentionDays", null),
        new("job runs", [JobRuns.Table], "JobRunRetentionDays", null),
    ];

    private static readonly StorageOptions _defaults = new();

    /// <inheritdoc />
    public IReadOnlyList<SettingDefinition> Definitions { get; } =
    [
        Keep("Logs", "LogRetentionDays", _defaults.LogRetentionDays, "VEYRA_LOG_RETENTION_DAYS", "logs"),
        Budget("Logs", "MaxLogStorageGigabytes", "VEYRA_MAX_LOG_STORAGE_GB", "logs"),
        Keep("Metrics", "MetricRetentionDays", _defaults.MetricRetentionDays, "VEYRA_METRIC_RETENTION_DAYS", "metrics"),
        new SettingDefinition(
            _prefix + "MaxMetricSeries",
            SettingSection.Data,
            "Metrics",
            "Series at most",
            "One attribute with too many values, such as a request ID, makes a series per value. Points of new series "
            + "beyond the limit are dropped, and the metric is named in Veyra's log.",
            SettingKind.WholeNumber,
            Text(_defaults.MaxMetricSeries))
        {
            Unit = "series",
            Minimum = 1,
            EnvironmentVariable = "VEYRA_MAX_METRIC_SERIES",
            Advanced = true,
        },
        Keep("Traces", "TraceRetentionDays", _defaults.TraceRetentionDays, "VEYRA_TRACE_RETENTION_DAYS", "traces"),
        Budget("Traces", "MaxTraceStorageGigabytes", "VEYRA_MAX_TRACE_STORAGE_GB", "traces"),
        Keep(
            "Database activity",
            "DatabaseSessionRetentionDays",
            _defaults.DatabaseSessionRetentionDays,
            "VEYRA_DATABASE_SESSION_RETENTION_DAYS",
            "live sessions"),
        Keep(
            "Database activity",
            "DatabaseRecordRetentionDays",
            _defaults.DatabaseRecordRetentionDays,
            "VEYRA_DATABASE_RECORD_RETENTION_DAYS",
            "query plans, deadlocks, and events") with { Advanced = true },
        Keep(
            "Jobs",
            "JobRunRetentionDays",
            _defaults.JobRunRetentionDays,
            "VEYRA_JOB_RUN_RETENTION_DAYS",
            "job runs") with { Advanced = true },
        new SettingDefinition(
            _prefix + "Jobs:NameAttributes",
            SettingSection.Data,
            "Jobs",
            "Job name attributes",
            "Span attributes that name a job, separated by commas, the first one found wins. A root span that carries "
            + "one is a run of that job, so Hangfire, Quartz.NET, hosted services, and pipelines show on the Jobs "
            + "screen. Empty recognises no traced jobs.",
            SettingKind.Text,
            _defaults.Jobs.NameAttributes)
        {
            Optional = true,
            Advanced = true,
        },
    ];

    /// <inheritdoc />
    public IReadOnlyList<SettingProblem> Validate(IConfiguration candidate) =>
        SettingChecks.Check(VeyraOptionNames.Storage, candidate, rules);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SettingNote>> ReviewAsync(
        IReadOnlyList<SettingChange> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var notes = new List<SettingNote>();

        foreach (var change in changes)
        {
            foreach (var signal in _signals)
            {
                if (change.Key == _prefix + signal.Retention
                    && await RetentionNoteAsync(signal, change, cancellationToken) is { } retention)
                {
                    notes.Add(retention);
                }

                if (signal.Budget is not null
                    && change.Key == _prefix + signal.Budget
                    && await BudgetNoteAsync(signal, change, cancellationToken) is { } budget)
                {
                    notes.Add(budget);
                }
            }
        }

        return notes;
    }

    private async Task<SettingNote?> RetentionNoteAsync(
        Signal signal,
        SettingChange change,
        CancellationToken cancellationToken)
    {
        var defaultDays = Days(Definitions.Single(definition => definition.Key == change.Key).Default);
        var from = Days(change.From) ?? defaultDays;
        var to = Days(change.To) ?? defaultDays;

        if (from is null || to is null || to >= from)
        {
            return null;
        }

        if (footprint is null)
        {
            return new SettingNote(
                change.Key,
                $"Keeps {to} days of {signal.Name} instead of {from}. Older {signal.Name} are deleted at the next "
                + $"hourly "
                + "clean-up and cannot be brought back.",
                Risky: true);
        }

        var (days, bytes) = await footprint.OlderThanAsync(signal.Tables, to.Value, cancellationToken);

        return days == 0
            ? new SettingNote(
                change.Key,
                $"No stored {signal.Name} are older than {to} days, so nothing is deleted now.",
                Risky: false)
            : new SettingNote(
                change.Key,
                $"Deletes {days} days of {signal.Name}, {Size(bytes)}, at the next hourly clean-up. Deleted "
                + $"{signal.Name} cannot be brought back.",
                Risky: true);
    }

    private async Task<SettingNote?> BudgetNoteAsync(
        Signal signal,
        SettingChange change,
        CancellationToken cancellationToken)
    {
        if (!double.TryParse(change.To, NumberStyles.Float, CultureInfo.InvariantCulture, out var gigabytes))
        {
            return null;
        }

        var budget = Size((long)(gigabytes * _bytesPerGigabyte));
        var name = char.ToUpperInvariant(signal.Name[0]) + signal.Name[1..];

        if (footprint is null)
        {
            return new SettingNote(
                change.Key,
                $"When {signal.Name} pass {budget}, the oldest days go first; today is always kept.",
                Risky: false);
        }

        var used = await footprint.TotalAsync(signal.Tables, cancellationToken);

        return used > gigabytes * _bytesPerGigabyte
            ? new SettingNote(
                change.Key,
                $"{name} use {Size(used)} now, more than {budget}: the oldest days are deleted at the next hourly "
                + $"clean-up until they fit; today is always kept. Deleted {signal.Name} cannot be brought back.",
                Risky: true)
            : new SettingNote(
                change.Key,
                $"{name} use {Size(used)} now. When they pass {budget}, the oldest days go first; today is always "
                + $"kept.",
                Risky: false);
    }

    private static SettingDefinition Keep(string group, string property, int days, string variable, string name) =>
        new(
            _prefix + property,
            SettingSection.Data,
            group,
            "Keep",
            $"How many days of {name} are kept. Older days are dropped whole, every hour.",
            SettingKind.WholeNumber,
            Text(days))
        {
            Unit = "days",
            Minimum = 1,
            Maximum = 3650,
            Effect = SettingEffect.DeletesData,
            EnvironmentVariable = variable,
        };

    private static SettingDefinition Budget(string group, string property, string variable, string name) =>
        new(
            _prefix + property,
            SettingSection.Data,
            group,
            "Size budget",
            $"Optional. When {name} take more room than this, the oldest days are dropped first; today is always kept.",
            SettingKind.Number,
            null)
        {
            Unit = "GB",
            Minimum = 0.1,
            Effect = SettingEffect.DeletesData,
            EnvironmentVariable = variable,
            Optional = true,
        };

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static int? Days(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) ? days : null;

    // 2.3 GB, 640 MB, 12 kB: one decimal below ten, none above.
    private static string Size(long bytes)
    {
        string[] units = ["B", "kB", "MB", "GB", "TB"];
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var format = value < 10 && unit > 0 ? "0.#" : "0";

        return value.ToString(format, CultureInfo.InvariantCulture) + " " + units[unit];
    }

    private sealed record Signal(string Name, IReadOnlyList<string> Tables, string Retention, string? Budget);
}
