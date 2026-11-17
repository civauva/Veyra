using Veyra.Alerting.Catalog;
using Veyra.Core.Settings;

namespace Veyra.Alerting;

/// <summary>
/// What <see cref="AlertingOptions"/> must meet, at startup and before the Settings screen saves a change. Each message
/// names the field to fix, so the screen can show it there (ADR 0017).
/// </summary>
public sealed class AlertingOptionsRules : OptionsRules<AlertingOptions>
{
    private const string _prefix = "Veyra:Alerting:";

    /// <inheritdoc />
    protected override IEnumerable<(bool Holds, string Message)> Rules(AlertingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        yield return (options.EvaluationInterval > TimeSpan.Zero, _prefix + "EvaluationInterval must be positive.");
        yield return (options.HistoryDays >= 1, _prefix + "HistoryDays must be at least 1.");
        yield return (
            string.IsNullOrWhiteSpace(options.PublicUrl)
                || (Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var url)
                    && (url.Scheme == Uri.UriSchemeHttps || url.Scheme == Uri.UriSchemeHttp)),
            _prefix + "PublicUrl must be a full http or https address, for example https://veyra.example.com.");

        var errorRate = options.ErrorRate;
        yield return (errorRate.Window > TimeSpan.Zero, _prefix + "ErrorRate:Window must be positive.");
        yield return (errorRate.MinimumRecords >= 1, _prefix + "ErrorRate:MinimumRecords must be at least 1.");
        yield return (errorRate.CautionRatio > 0, _prefix + "ErrorRate:CautionRatio must be above 0 %.");
        yield return (
            errorRate.WarningRatio >= errorRate.CautionRatio,
            _prefix + "ErrorRate:WarningRatio must not be below the caution level.");
        yield return (errorRate.WarningRatio <= 1, _prefix + "ErrorRate:WarningRatio must not be above 100 %.");

        var diskSpace = options.DiskSpace;
        yield return (diskSpace.CautionRatio > 0, _prefix + "DiskSpace:CautionRatio must be above 0 %.");
        yield return (
            diskSpace.WarningRatio >= diskSpace.CautionRatio,
            _prefix + "DiskSpace:WarningRatio must not be below the caution level.");
        yield return (diskSpace.WarningRatio <= 1, _prefix + "DiskSpace:WarningRatio must not be above 100 %.");
        yield return (
            diskSpace.ClearMargin >= 0 && diskSpace.ClearMargin < diskSpace.CautionRatio,
            _prefix + "DiskSpace:ClearMargin must be at least 0 and below the caution level.");

        var diskFilling = options.DiskFilling;
        yield return (diskFilling.WarningDays > 0, _prefix + "DiskFilling:WarningDays must be above 0.");
        yield return (
            diskFilling.WarningDays <= diskFilling.CautionDays,
            _prefix + "DiskFilling:WarningDays must not be more than the caution level.");
        yield return (diskFilling.ClearFactor >= 1, _prefix + "DiskFilling:ClearFactor must be 1 or more.");

        var latency = options.Latency;
        yield return (latency.CautionSeconds > 0, _prefix + "Latency:CautionSeconds must be above 0.");
        yield return (
            latency.WarningSeconds >= latency.CautionSeconds,
            _prefix + "Latency:WarningSeconds must not be below the caution level.");
        yield return (
            latency.ClearFactor is > 0 and <= 1,
            _prefix + "Latency:ClearFactor must be above 0 and at most 100 %.");
        yield return (latency.MinimumRequests >= 1, _prefix + "Latency:MinimumRequests must be at least 1.");

        var restarts = options.ContainerRestarts;
        yield return (
            restarts.CautionRestarts >= 1,
            _prefix + "ContainerRestarts:CautionRestarts must be at least 1.");
        yield return (
            restarts.WarningRestarts >= restarts.CautionRestarts,
            _prefix + "ContainerRestarts:WarningRestarts must not be below the caution level.");

        var memory = options.ContainerMemory;
        yield return (memory.CautionRatio > 0, _prefix + "ContainerMemory:CautionRatio must be above 0 %.");
        yield return (
            memory.WarningRatio >= memory.CautionRatio,
            _prefix + "ContainerMemory:WarningRatio must not be below the caution level.");
        yield return (memory.WarningRatio <= 1, _prefix + "ContainerMemory:WarningRatio must not be above 100 %.");
        yield return (
            memory.ClearMargin >= 0 && memory.ClearMargin < memory.CautionRatio,
            _prefix + "ContainerMemory:ClearMargin must be at least 0 and below the caution level.");

        var blocking = options.DatabaseBlocking;
        yield return (
            blocking.CautionSessions >= 1,
            _prefix + "DatabaseBlocking:CautionSessions must be at least 1.");
        yield return (
            blocking.WarningSessions >= blocking.CautionSessions,
            _prefix + "DatabaseBlocking:WarningSessions must not be below the caution level.");

        var deadlocks = options.DatabaseDeadlocks;
        yield return (
            deadlocks.CautionDeadlocks >= 1,
            _prefix + "DatabaseDeadlocks:CautionDeadlocks must be at least 1.");
        yield return (
            deadlocks.WarningDeadlocks >= deadlocks.CautionDeadlocks,
            _prefix + "DatabaseDeadlocks:WarningDeadlocks must not be below the caution level.");

        var longQuery = options.DatabaseLongQuery;
        yield return (
            longQuery.CautionMinutes > 0,
            _prefix + "DatabaseLongQuery:CautionMinutes must be above 0.");
        yield return (
            longQuery.WarningMinutes >= longQuery.CautionMinutes,
            _prefix + "DatabaseLongQuery:WarningMinutes must not be below the caution level.");

        var backupAge = options.DatabaseBackupAge;
        yield return (
            backupAge.CautionHours > 0,
            _prefix + "DatabaseBackupAge:CautionHours must be above 0.");
        yield return (
            backupAge.WarningHours >= backupAge.CautionHours,
            _prefix + "DatabaseBackupAge:WarningHours must not be below the caution level.");

        var connections = options.DatabaseConnections;
        yield return (
            connections.CautionRatio > 0,
            _prefix + "DatabaseConnections:CautionRatio must be above 0 %.");
        yield return (
            connections.WarningRatio >= connections.CautionRatio,
            _prefix + "DatabaseConnections:WarningRatio must not be below the caution level.");
        yield return (
            connections.WarningRatio <= 1,
            _prefix + "DatabaseConnections:WarningRatio must not be above 100 %.");
        yield return (
            connections.ClearMargin >= 0 && connections.ClearMargin < connections.CautionRatio,
            _prefix + "DatabaseConnections:ClearMargin must be at least 0 and below the caution level.");

        var wraparound = options.DatabaseWraparound;
        yield return (
            wraparound.CautionRatio > 0,
            _prefix + "DatabaseWraparound:CautionRatio must be above 0 %.");
        yield return (
            wraparound.WarningRatio >= wraparound.CautionRatio,
            _prefix + "DatabaseWraparound:WarningRatio must not be below the caution level.");
        yield return (
            wraparound.WarningRatio <= 1,
            _prefix + "DatabaseWraparound:WarningRatio must not be above 100 %.");
        yield return (
            wraparound.ClearRatio >= 0 && wraparound.ClearRatio <= wraparound.CautionRatio,
            _prefix + "DatabaseWraparound:ClearRatio must be at least 0 and not above the caution level.");

        foreach (var rule in RuleCatalog.All)
        {
            var (caution, warning) = RuleCatalog.Levels(rule, options);
            var key = $"{_prefix}{RuleCatalog.Section}:{rule.Setting}";

            yield return (warning >= 0 && caution is null or >= 0, key + " levels must not be below 0.");
            yield return (
                caution is null || (rule.LowerIsWorse ? warning <= caution : warning >= caution),
                key + (rule.LowerIsWorse
                    ? ":Warning must not be above the caution level."
                    : ":Warning must not be below the caution level."));
        }

        var notifications = options.Notifications;
        yield return (
            notifications.DispatchInterval > TimeSpan.Zero,
            _prefix + "Notifications:DispatchInterval must be positive.");
        yield return (
            notifications.SendTimeout > TimeSpan.Zero,
            _prefix + "Notifications:SendTimeout must be positive.");
        yield return (notifications.BatchSize >= 1, _prefix + "Notifications:BatchSize must be at least 1.");
        yield return (
            notifications.Smtp.Port is >= 1 and <= 65535,
            _prefix + "Notifications:Smtp:Port must be between 1 and 65535.");
        yield return (
            !notifications.Smtp.IsConfigured || MimeKit.MailboxAddress.TryParse(notifications.Smtp.From, out _),
            _prefix + "Notifications:Smtp:From must be the sender's email address when a server is set "
            + "(VEYRA_SMTP_FROM).");
    }
}
