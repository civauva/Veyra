using System.Globalization;
using Veyra.Core.Activity;
using Veyra.Core.Telemetry;
using Veyra.Query.Queries.Api;
using Veyra.Query.Server.Api;

namespace Veyra.Query.Server;

/// <summary>
/// Builds a SQL Server's configuration and security posture (ADR 0020, area 13) from the agent's hourly snapshots:
/// settings off their recommended values, security findings with their fix, and every change with its time.
/// </summary>
/// <param name="activity">The snapshot records.</param>
/// <param name="clock">The clock that decides what now is.</param>
internal sealed class ConfigurationViewBuilder(IDatabaseActivitySearch activity, TimeProvider clock)
{
    /// <summary>The kind of record a snapshot row is.</summary>
    public const string SnapshotKind = "sqlserver.snapshot";

    // A value not seen again for this long is gone, such as a sysadmin removed: snapshots keep each value once a day.
    private static readonly TimeSpan _gone = TimeSpan.FromHours(26);

    /// <summary>Builds the configuration of a server.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="server">The server.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings, findings, and changes of the last 30 days.</returns>
    public async Task<ConfigurationResponse> ConfigurationAsync(
        string engine,
        string server,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var records = await activity.ListRecordsAsync(
            TelemetryBlock.Database(engine, server).Name,
            SnapshotKind,
            now.AddDays(-30),
            now,
            null,
            20_000,
            cancellationToken);

        return Build(records, now);
    }

    /// <summary>Turns the snapshots into the configuration. Pure; tested without a database.</summary>
    /// <param name="records">The snapshot rows of the last 30 days.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The configuration.</returns>
    public static ConfigurationResponse Build(IReadOnlyList<ActivityRecord> records, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(records);

        string Type(ActivityRecord record) => record.Attributes.GetValueOrDefault("row_type") ?? "";
        string Item(ActivityRecord record) => record.Attributes.GetValueOrDefault("item") ?? "";

        var settings = records.Where(record => Type(record) == "setting")
            .GroupBy(Item)
            .ToDictionary(
                group => group.Key,
                group => group.MaxBy(record => record.SampledAt)!.Body,
                StringComparer.OrdinalIgnoreCase);
        var security = records.Where(record => Type(record) == "security" && now - record.SampledAt <= _gone)
            .GroupBy(record => (Item: Item(record), record.Body))
            .Select(group => group.Key)
            .ToList();
        var changes = records.Where(record => Type(record) == "setting")
            .GroupBy(Item)
            .SelectMany(group =>
            {
                var values = group.GroupBy(record => record.Body)
                    .Select(value => (Value: value.Key, First: value.Min(record => record.SampledAt)))
                    .OrderBy(value => value.First)
                    .ToList();

                return values
                    .Skip(1)
                    .Select((value, index) => new ConfigurationChangeResponse(
                        value.First,
                        group.Key,
                        values[index].Value,
                        value.Value));
            })
            .Concat(records.Where(record => Type(record) == "security" && Item(record) == "sysadmin")
                .GroupBy(record => record.Body)
                .Select(group => group.Min(record => record.SampledAt) is var first
                    && first > records.Min(record => record.SampledAt)
                    ? new ConfigurationChangeResponse(first, "sysadmin", "", group.Key)
                    : null)
                .OfType<ConfigurationChangeResponse>())
            .OrderByDescending(change => change.At)
            .ToList();

        return new ConfigurationResponse(
            Recommended.Select(setting => new SettingResponse(
                    setting.Name,
                    settings.GetValueOrDefault(setting.Name) ?? "",
                    setting.Recommended))
                .ToList(),
            (IReadOnlyList<PlanWarningResponse>)Findings(settings, security, now),
            security.Where(entry => entry.Item == "sysadmin").Select(entry => entry.Body).Order().ToList(),
            changes);
    }

    /// <summary>The settings worth knowing, with what Veyra recommends.</summary>
    public static readonly IReadOnlyList<(string Name, string Recommended)> Recommended =
    [
        ("max server memory (MB)", "set below the machine's memory"),
        ("max degree of parallelism", "the CPUs of one NUMA node, at most 8"),
        ("cost threshold for parallelism", "50"),
        ("optimize for ad hoc workloads", "1"),
        ("xp_cmdshell", "0"),
        ("clr enabled", "0"),
        ("priority boost", "0"),
        ("lightweight pooling", "0"),
        ("remote admin connections", "1"),
        ("backup compression default", "1"),
    ];

    private static List<PlanWarningResponse> Findings(
        Dictionary<string, string> settings,
        List<(string Item, string Body)> security,
        DateTimeOffset now)
    {
        var findings = new List<PlanWarningResponse>();
        string? Setting(string name) => settings.GetValueOrDefault(name);
        string Configure(string name, string value) =>
            $"EXEC sp_configure 'show advanced options', 1; RECONFIGURE; EXEC sp_configure '{name}', {value}; "
            + "RECONFIGURE;";

        if (Setting("max server memory (MB)") == "2147483647")
        {
            findings.Add(
                new(
                    "max-memory",
                    "caution",
                    "Maximum server memory is not set",
                    "SQL Server may take nearly all of the machine's memory and starve Windows or Linux and other "
                    + "processes.",
                    "Set it to leave the operating system a few GB, more on larger machines.",
                    Configure("max server memory (MB)", "<MB>")));
        }

        if (Setting("cost threshold for parallelism") == "5")
        {
            findings.Add(
                new(
                    "cost-threshold",
                    "caution",
                    "Cost threshold for parallelism is still 5",
                    "Even small queries run in parallel, which costs more than it gains.",
                    "Raise it to about 50 and adjust from there.",
                    Configure("cost threshold for parallelism", "50")));
        }

        if (Setting("optimize for ad hoc workloads") == "0")
        {
            findings.Add(
                new(
                    "ad-hoc",
                    "caution",
                    "Optimize for ad hoc workloads is off",
                    "Every statement run once keeps a whole plan in the cache.",
                    "Turn it on.",
                    Configure("optimize for ad hoc workloads", "1")));
        }

        if (Setting("xp_cmdshell") == "1")
        {
            findings.Add(
                new(
                    "xp-cmdshell",
                    "warning",
                    "xp_cmdshell is on",
                    "Anyone who gets sysadmin can run any program on the machine.",
                    "Turn it off unless a job needs it.",
                    Configure("xp_cmdshell", "0")));
        }

        if (Setting("priority boost") == "1")
        {
            findings.Add(
                new(
                    "priority-boost",
                    "warning",
                    "Priority boost is on",
                    "SQL Server can starve the operating system; Microsoft advises against it.",
                    "Turn it off.",
                    Configure("priority boost", "0")));
        }

        if (Setting("clr enabled") == "1")
        {
            findings.Add(
                new(
                    "clr",
                    "caution",
                    "CLR is enabled",
                    ".NET code can run inside SQL Server.",
                    "Turn it off unless an application needs it.",
                    Configure("clr enabled", "0")));
        }

        if (security.Any(entry => entry.Item == "sa enabled" && entry.Body == "1"))
        {
            findings.Add(
                new(
                    "sa",
                    "caution",
                    "The sa login is enabled",
                    "Every attack tries sa first, with a known name.",
                    "Disable it; administrators use their own logins.",
                    "ALTER LOGIN [sa] DISABLE;"));
        }

        foreach (var login in security.Where(entry => entry.Item == "login without password policy"))
        {
            findings.Add(
                new(
                    "password-policy",
                    "caution",
                    $"Login {login.Body} skips the password policy",
                    "Its password can be short and never expire.",
                    "Turn the policy on.",
                    $"ALTER LOGIN [{login.Body.Replace("]", "]]", StringComparison.Ordinal)}] WITH CHECK_POLICY = "
                    + "ON;"));
        }

        foreach (var database in security.Where(entry => entry.Item == "trustworthy"))
        {
            findings.Add(
                new(
                    "trustworthy",
                    "warning",
                    $"Database {database.Body} is TRUSTWORTHY",
                    "Code in it can act with its owner's server rights, often sysadmin.",
                    "Turn it off unless it is needed.",
                    $"ALTER DATABASE [{database.Body.Replace("]", "]]", StringComparison.Ordinal)}] SET TRUSTWORTHY "
                    + "OFF;"));
        }

        foreach (var certificate in security.Where(entry => entry.Item == "certificate"))
        {
            var parts = certificate.Body.Split('|');

            if (parts.Length == 2 && long.TryParse(parts[1], CultureInfo.InvariantCulture, out var expires))
            {
                var left = DateTimeOffset.FromUnixTimeSeconds(expires) - now;

                if (left < TimeSpan.FromDays(30))
                {
                    findings.Add(
                        new(
                            "certificate",
                            left < TimeSpan.Zero ? "warning" : "caution",
                            left < TimeSpan.Zero
                                ? $"Certificate {parts[0]} has expired"
                                : $"Certificate {parts[0]} expires in {Math.Max(1, (int)left.TotalDays)} days",
                            "Backups or encryption that use it may stop working, and a restore elsewhere needs it.",
                            "Renew it and back up the new one with its private key.",
                            null));
                }
            }
        }

        if (security.FirstOrDefault(entry => entry.Item == "unencrypted connections") is { Body: var count }
            && int.TryParse(count, CultureInfo.InvariantCulture, out var unencrypted) && unencrypted > 0)
        {
            findings.Add(
                new(
                    "unencrypted",
                    "caution",
                    $"{unencrypted} connections are not encrypted",
                    "Logins and data travel the network in the clear.",
                    "Require encryption in the applications' connection strings, or force it on the server.",
                    null));
        }

        return findings.OrderBy(finding => finding.Severity == "warning" ? 0 : 1).ToList();
    }
}
