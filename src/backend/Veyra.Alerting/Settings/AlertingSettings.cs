using System.Globalization;
using Microsoft.Extensions.Options;
using Veyra.Alerting.Catalog;
using Veyra.Alerting.Notifications;
using Veyra.Core;
using Veyra.Core.Settings;

namespace Veyra.Alerting.Settings;

/// <summary>
/// The Alerting module's settings on the Settings screen: the built-in rules, how long resolved alerts are kept, the
/// email server, and how queued messages are sent (ADR 0017).
/// </summary>
/// <param name="rules">The rules <see cref="AlertingOptions"/> must meet.</param>
internal sealed class AlertingSettings(IEnumerable<IValidateOptions<AlertingOptions>> rules)
    : ModuleSettings<AlertingOptions>(VeyraOptionNames.Alerting, rules)
{
    private const string _prefix = VeyraOptionNames.Alerting + ":";

    private static readonly AlertingOptions _defaults = new();

    // What the Simple view leaves out: each rule keeps its switch and its two levels; how it measures and clears,
    // how often rules run, and how queued messages are sent are for the Advanced view.
    private static readonly HashSet<string> _advanced =
    [
        _prefix + "ErrorRate:Window",
        _prefix + "ErrorRate:MinimumRecords",
        _prefix + "Latency:ClearFactor",
        _prefix + "Latency:MinimumRequests",
        _prefix + "DiskSpace:ClearMargin",
        _prefix + "DiskFilling:ClearFactor",
        _prefix + "ContainerMemory:ClearMargin",
        _prefix + "DatabaseConnections:ClearMargin",
        _prefix + "DatabaseWraparound:ClearRatio",
        _prefix + "DatabaseBackupAge:IgnoredDatabases",
        _prefix + "EvaluationInterval",
        _prefix + "HistoryDays",
        _prefix + "Notifications:DispatchInterval",
        _prefix + "Notifications:SendTimeout",
        _prefix + "Notifications:BatchSize",
    ];

    // The rule names people see, by the section of each rule's settings.
    private static readonly Dictionary<string, string> _ruleNames = new(StringComparer.Ordinal)
    {
        ["ErrorRate"] = "The error-rate rule",
        ["Latency"] = "The latency rule",
        ["DiskSpace"] = "The disk-space rule",
        ["DiskFilling"] = "The disk-filling rule",
        ["ContainerRestarts"] = "The container restart rule",
        ["ContainerMemory"] = "The container memory rule",
        ["DatabaseBlocking"] = "The database blocking rule",
        ["DatabaseDeadlocks"] = "The deadlock rule",
        ["DatabaseLongQuery"] = "The long-query rule",
        ["DatabaseBackupAge"] = "The backup-age rule",
        ["DatabaseAgentJob"] = "The SQL Agent job rule",
        ["DatabaseConnections"] = "The connections rule",
        ["DatabaseWraparound"] = "The wraparound rule",
    };

    /// <inheritdoc />
    public override IReadOnlyList<SettingDefinition> Definitions { get; } = Build()
        .Select(definition => definition with { Advanced = _advanced.Contains(definition.Key) })
        .ToList();

    /// <inheritdoc />
    public override Task<IReadOnlyList<SettingNote>> ReviewAsync(
        IReadOnlyList<SettingChange> changes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var notes = new List<SettingNote>();

        foreach (var change in changes)
        {
            if (change.Key == _prefix + nameof(AlertingOptions.HistoryDays)
                && Whole(change.To) is { } to
                && to < (Whole(change.From) ?? _defaults.HistoryDays))
            {
                notes.Add(new SettingNote(
                    change.Key,
                    $"Resolved alerts older than {to} days are deleted at the next rule run and cannot be brought "
                    + $"back.",
                    Risky: true));
            }

            if (change.Key.EndsWith(":Enabled", StringComparison.Ordinal)
                && string.Equals(change.To, "false", StringComparison.OrdinalIgnoreCase)
                && RuleName(change.Key[_prefix.Length..^":Enabled".Length]) is { } rule)
            {
                notes.Add(new SettingNote(
                    change.Key,
                    $"{rule} opens no new alerts while it is off. Its open alerts stay until they clear.",
                    Risky: false));
            }
        }

        return Task.FromResult<IReadOnlyList<SettingNote>>(notes);
    }

    private static List<SettingDefinition> Build()
    {
        var errorRate = _defaults.ErrorRate;
        var diskSpace = _defaults.DiskSpace;
        var diskFilling = _defaults.DiskFilling;
        var latency = _defaults.Latency;
        var restarts = _defaults.ContainerRestarts;
        var containerMemory = _defaults.ContainerMemory;
        var blocking = _defaults.DatabaseBlocking;
        var deadlocks = _defaults.DatabaseDeadlocks;
        var longQuery = _defaults.DatabaseLongQuery;
        var backupAge = _defaults.DatabaseBackupAge;
        var connections = _defaults.DatabaseConnections;
        var wraparound = _defaults.DatabaseWraparound;
        var notifications = _defaults.Notifications;
        var smtp = notifications.Smtp;

        return
        [
            Switch("ErrorRate", "Error rate", "Whether the error-rate rule runs."),
            Ratio(
                "ErrorRate:CautionRatio",
                "Error rate",
                "Caution from",
                "Share of a service's log records that are errors that raises a caution.",
                errorRate.CautionRatio),
            Ratio(
                "ErrorRate:WarningRatio",
                "Error rate",
                "Warning from",
                "Share of errors that raises a warning.",
                errorRate.WarningRatio),
            Duration(
                SettingSection.Rules,
                "ErrorRate:Window",
                "Error rate",
                "Measured over",
                "The window the share is measured over.",
                errorRate.Window,
                "min"),
            Whole(
                SettingSection.Rules,
                "ErrorRate:MinimumRecords",
                "Error rate",
                "At least",
                "Fewer records in the window never alert, so one failed request on a quiet service is not a storm.",
                errorRate.MinimumRecords,
                "records"),

            Switch("Latency", "Latency", "Whether the latency rule runs."),
            Number(
                SettingSection.Rules,
                "Latency:CautionSeconds",
                "Latency",
                "Caution from",
                "p95 of a service's requests over 5 minutes that raises a caution.",
                latency.CautionSeconds,
                "s"),
            Number(
                SettingSection.Rules,
                "Latency:WarningSeconds",
                "Latency",
                "Warning from",
                "p95 that raises a warning.",
                latency.WarningSeconds,
                "s"),
            Ratio(
                "Latency:ClearFactor",
                "Latency",
                "Clears below",
                "An alert clears once the p95 is below this share of the level it crossed.",
                latency.ClearFactor,
                "% of the level"),
            Whole(
                SettingSection.Rules,
                "Latency:MinimumRequests",
                "Latency",
                "At least",
                "Fewer requests in 5 minutes never alert.",
                latency.MinimumRequests,
                "requests"),

            Switch("DiskSpace", "Disk space", "Whether the disk-space rule runs."),
            Ratio(
                "DiskSpace:CautionRatio",
                "Disk space",
                "Caution from",
                "Share of a disk in use that raises a caution.",
                diskSpace.CautionRatio),
            Ratio(
                "DiskSpace:WarningRatio",
                "Disk space",
                "Warning from",
                "Share of a disk in use that raises a warning.",
                diskSpace.WarningRatio),
            Ratio(
                "DiskSpace:ClearMargin",
                "Disk space",
                "Clears",
                "An alert clears this many points below the level it crossed.",
                diskSpace.ClearMargin,
                "points below"),

            Switch("DiskFilling", "Disk filling", "Whether the disk-filling rule runs."),
            Number(
                SettingSection.Rules,
                "DiskFilling:CautionDays",
                "Disk filling",
                "Caution within",
                "Days until full, at the last day's pace, that raise a caution.",
                diskFilling.CautionDays,
                "days"),
            Number(
                SettingSection.Rules,
                "DiskFilling:WarningDays",
                "Disk filling",
                "Warning within",
                "Days until full that raise a warning.",
                diskFilling.WarningDays,
                "days"),
            Number(
                SettingSection.Rules,
                "DiskFilling:ClearFactor",
                "Disk filling",
                "Clears beyond",
                "An alert clears once the days until full are this many times the caution level.",
                diskFilling.ClearFactor,
                "× the caution level"),

            Switch("ContainerRestarts", "Container restarts", "Whether the restart rule for containers runs."),
            Whole(
                SettingSection.Rules,
                "ContainerRestarts:CautionRestarts",
                "Container restarts",
                "Caution from",
                "Restarts of a container in 15 minutes that raise a caution. It clears after 15 minutes without one.",
                restarts.CautionRestarts,
                "restarts"),
            Whole(
                SettingSection.Rules,
                "ContainerRestarts:WarningRestarts",
                "Container restarts",
                "Warning from",
                "Restarts in 15 minutes that raise a warning.",
                restarts.WarningRestarts,
                "restarts"),

            Switch("ContainerMemory", "Container memory", "Whether the memory rule for containers runs."),
            Ratio(
                "ContainerMemory:CautionRatio",
                "Container memory",
                "Caution from",
                "Share of its memory limit a container uses that raises a caution. Without a limit, Docker counts "
                + "the host's memory.",
                containerMemory.CautionRatio),
            Ratio(
                "ContainerMemory:WarningRatio",
                "Container memory",
                "Warning from",
                "Share of its memory limit in use that raises a warning.",
                containerMemory.WarningRatio),
            Ratio(
                "ContainerMemory:ClearMargin",
                "Container memory",
                "Clears",
                "An alert clears this many points below the level it crossed.",
                containerMemory.ClearMargin,
                "points below"),

            Switch("DatabaseBlocking", "Database blocking", "Whether the blocking rule for database servers runs."),
            Whole(
                SettingSection.Rules,
                "DatabaseBlocking:CautionSessions",
                "Database blocking",
                "Caution from",
                "Sessions blocked throughout 5 minutes that raise a caution. It clears after 5 minutes without one.",
                blocking.CautionSessions,
                "sessions"),
            Whole(
                SettingSection.Rules,
                "DatabaseBlocking:WarningSessions",
                "Database blocking",
                "Warning from",
                "Sessions blocked throughout 5 minutes that raise a warning.",
                blocking.WarningSessions,
                "sessions"),

            Switch("DatabaseDeadlocks", "Deadlocks", "Whether the deadlock rule for database servers runs."),
            Whole(
                SettingSection.Rules,
                "DatabaseDeadlocks:CautionDeadlocks",
                "Deadlocks",
                "Caution from",
                "Deadlocks in 15 minutes that raise a caution. It clears after 30 minutes without one.",
                deadlocks.CautionDeadlocks,
                "deadlocks"),
            Whole(
                SettingSection.Rules,
                "DatabaseDeadlocks:WarningDeadlocks",
                "Deadlocks",
                "Warning from",
                "Deadlocks in 15 minutes that raise a warning.",
                deadlocks.WarningDeadlocks,
                "deadlocks"),

            Switch("DatabaseLongQuery", "Long query", "Whether the long-query rule for databases runs."),
            Number(
                SettingSection.Rules,
                "DatabaseLongQuery:CautionMinutes",
                "Long query",
                "Caution from",
                "How long a query has run that raises a caution for its database.",
                longQuery.CautionMinutes,
                "min"),
            Number(
                SettingSection.Rules,
                "DatabaseLongQuery:WarningMinutes",
                "Long query",
                "Warning from",
                "How long a query has run that raises a warning.",
                longQuery.WarningMinutes,
                "min"),

            Switch("DatabaseBackupAge", "Backup age", "Whether the backup-age rule for databases runs."),
            Number(
                SettingSection.Rules,
                "DatabaseBackupAge:CautionHours",
                "Backup age",
                "Caution from",
                "Hours since a database's last full backup that raise a caution. A new backup clears it.",
                backupAge.CautionHours,
                "h"),
            Number(
                SettingSection.Rules,
                "DatabaseBackupAge:WarningHours",
                "Backup age",
                "Warning from",
                "Hours since the last full backup that raise a warning.",
                backupAge.WarningHours,
                "h"),
            new SettingDefinition(
                _prefix + "DatabaseBackupAge:IgnoredDatabases",
                SettingSection.Rules,
                "Backup age",
                "Not backed up on purpose",
                "Databases the rule leaves alone, by name, separated by commas. Databases never backed up are left "
                + "alone anyway.",
                SettingKind.Text,
                backupAge.IgnoredDatabases)
            {
                Optional = true,
            },

            Switch("DatabaseAgentJob", "SQL Agent jobs", "Whether the rule for failed SQL Agent jobs runs."),

            Switch(
                "DatabaseConnections",
                "Database connections",
                "Whether the connections rule for database servers runs. Only PostgreSQL says its maximum."),
            Ratio(
                "DatabaseConnections:CautionRatio",
                "Database connections",
                "Caution from",
                "Share of the server's maximum connections in use that raises a caution. At the maximum, new "
                + "connections are refused.",
                connections.CautionRatio),
            Ratio(
                "DatabaseConnections:WarningRatio",
                "Database connections",
                "Warning from",
                "Share of the maximum connections in use that raises a warning.",
                connections.WarningRatio),
            Ratio(
                "DatabaseConnections:ClearMargin",
                "Database connections",
                "Clears",
                "An alert clears this many points below the level it crossed.",
                connections.ClearMargin,
                "points below"),

            Switch(
                "DatabaseWraparound",
                "Transaction ID wraparound",
                "Whether the wraparound rule for PostgreSQL databases runs."),
            Ratio(
                "DatabaseWraparound:CautionRatio",
                "Transaction ID wraparound",
                "Caution from",
                "Age of a database's oldest transaction ID, of the limit of about 2.1 billion, that raises a caution. "
                + "Near the limit PostgreSQL stops accepting writes until the database is vacuumed.",
                wraparound.CautionRatio),
            Ratio(
                "DatabaseWraparound:WarningRatio",
                "Transaction ID wraparound",
                "Warning from",
                "Age of the oldest transaction ID, of the limit, that raises a warning.",
                wraparound.WarningRatio),
            Ratio(
                "DatabaseWraparound:ClearRatio",
                "Transaction ID wraparound",
                "Clears below",
                "A caution clears once the age falls below this share of the limit, as a vacuum freezes old rows.",
                wraparound.ClearRatio),

            .. RuleCatalog.All.SelectMany(CatalogDefinitions),

            Duration(
                SettingSection.Rules,
                "EvaluationInterval",
                "Checking",
                "Check the rules every",
                "How often every rule runs.",
                _defaults.EvaluationInterval,
                "s"),
            Whole(
                SettingSection.Rules,
                "HistoryDays",
                "Checking",
                "Keep resolved alerts",
                "Resolved alerts older than this are deleted.",
                _defaults.HistoryDays,
                "days") with { Effect = SettingEffect.DeletesData, Maximum = 3650 },

            Text(
                SettingSection.Email,
                "Notifications:Smtp:Host",
                "Email server",
                "Server",
                "The SMTP server every email channel sends through. Without one, email channels cannot send.",
                smtp.Host,
                "VEYRA_SMTP_HOST"),
            Whole(
                SettingSection.Email,
                "Notifications:Smtp:Port",
                "Email server",
                "Port",
                "Usually 587 with STARTTLS or 465 with TLS from the start.",
                smtp.Port,
                null) with { Maximum = 65535, EnvironmentVariable = "VEYRA_SMTP_PORT" },
            new SettingDefinition(
                _prefix + "Notifications:Smtp:Security",
                SettingSection.Email,
                "Email server",
                "Security",
                "Auto picks TLS for port 465 and STARTTLS when the server offers it.",
                SettingKind.Choice,
                smtp.Security.ToString())
            {
                Choices = Enum.GetNames<SmtpSecurity>(),
                EnvironmentVariable = "VEYRA_SMTP_SECURITY",
            },
            Text(
                SettingSection.Email,
                "Notifications:Smtp:From",
                "Email server",
                "From",
                "The sender, such as Veyra <alerts@example.com>.",
                smtp.From,
                "VEYRA_SMTP_FROM"),
            Text(
                SettingSection.Email,
                "Notifications:Smtp:Username",
                "Email server",
                "User name",
                "Leave empty when the server needs no sign-in.",
                smtp.Username,
                "VEYRA_SMTP_USERNAME"),
            new SettingDefinition(
                _prefix + "Notifications:Smtp:Password",
                SettingSection.Email,
                "Email server",
                "Password",
                "Stored encrypted and never shown again.",
                SettingKind.Secret,
                null)
            {
                Optional = true,
                EnvironmentVariable = "VEYRA_SMTP_PASSWORD",
            },
            Text(
                SettingSection.Email,
                "PublicUrl",
                "Links",
                "Veyra's address",
                "Messages link to the alert with it, such as https://veyra.example.com.",
                _defaults.PublicUrl,
                "VEYRA_PUBLIC_URL"),

            Duration(
                SettingSection.Email,
                "Notifications:DispatchInterval",
                "Sending",
                "Send queued messages every",
                "How often the outbox is checked for messages to send.",
                notifications.DispatchInterval,
                "s"),
            Duration(
                SettingSection.Email,
                "Notifications:SendTimeout",
                "Sending",
                "Give up on a try after",
                "A try that takes longer counts as failed and is retried later.",
                notifications.SendTimeout,
                "s"),
            Whole(
                SettingSection.Email,
                "Notifications:BatchSize",
                "Sending",
                "Messages per round",
                "The most messages sent in one round.",
                notifications.BatchSize,
                "messages"),
        ];
    }

    // A built-in rule's name in sentences, by the section of its settings; catalog rules sit under Catalog.
    private static string? RuleName(string section) =>
        _ruleNames.GetValueOrDefault(section)
        ?? RuleCatalog.All
            .Where(rule => section == $"{RuleCatalog.Section}:{rule.Setting}")
            .Select(rule => $"The {rule.Name.ToLowerInvariant()} rule")
            .FirstOrDefault();

    // A catalog rule's switch and levels, in the unit the Settings screen shows (ADR 0020).
    private static IEnumerable<SettingDefinition> CatalogDefinitions(CatalogRule rule)
    {
        var section = $"{RuleCatalog.Section}:{rule.Setting}";

        yield return Switch(section, rule.Name, $"Whether the {rule.Name.ToLowerInvariant()} rule runs.");

        if (rule.Unit == MeasureUnit.Flag)
        {
            yield break;
        }

        var lower = rule.LowerIsWorse ? "at or below" : "from";

        if (rule.DefaultCaution is { } caution)
        {
            yield return Level(
                rule,
                $"{section}:Caution",
                "Caution from",
                $"{rule.Description} A caution {lower} this.",
                caution);
        }

        yield return Level(rule, $"{section}:Warning", "Warning from", $"A warning {lower} this.", rule.DefaultWarning);
    }

    private static SettingDefinition Level(CatalogRule rule, string key, string label, string help, double value) =>
        rule.Unit == MeasureUnit.Ratio
            ? Ratio(key, rule.Name, label, help, value)
            : Number(
                SettingSection.Rules,
                key,
                rule.Name,
                label,
                help,
                value / rule.SettingScale,
                rule.SettingUnit);

    private static SettingDefinition Switch(string rule, string group, string help) => new(
        $"{_prefix}{rule}:Enabled",
        SettingSection.Rules,
        group,
        "On",
        help + " A rule switched off opens no new alerts.",
        SettingKind.Switch,
        "true");

    private static SettingDefinition Ratio(
        string key,
        string group,
        string label,
        string help,
        double value,
        string unit = "%") =>
        new(_prefix + key, SettingSection.Rules, group, label, help, SettingKind.Ratio, Invariant(value))
        {
            Unit = unit,
            Minimum = 0,
            Maximum = 100,
        };

    private static SettingDefinition Number(
        SettingSection section,
        string key,
        string group,
        string label,
        string help,
        double value,
        string unit) =>
        new(
            _prefix + key,
            section,
            group,
            label,
            help,
            SettingKind.Number,
            Invariant(value))
        { Unit = unit, Minimum = 0 };

    private static SettingDefinition Whole(
        SettingSection section,
        string key,
        string group,
        string label,
        string help,
        int value,
        string? unit) =>
        new(
            _prefix + key,
            section,
            group,
            label,
            help,
            SettingKind.WholeNumber,
            Invariant(value))
        { Unit = unit, Minimum = 1 };

    private static SettingDefinition Duration(
        SettingSection section,
        string key,
        string group,
        string label,
        string help,
        TimeSpan value,
        string unit) =>
        new(
            _prefix + key,
            section,
            group,
            label,
            help,
            SettingKind.Duration,
            value.ToString("c", CultureInfo.InvariantCulture))
        {
            Unit = unit,
            Minimum = 1,
        };

    private static SettingDefinition Text(
        SettingSection section,
        string key,
        string group,
        string label,
        string help,
        string? value,
        string variable) =>
        new(
            _prefix + key,
            section,
            group,
            label,
            help,
            SettingKind.Text,
            value)
        { Optional = true, EnvironmentVariable = variable };

    private static string Invariant(IFormattable value) => value.ToString(null, CultureInfo.InvariantCulture);

    private static int? Whole(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
