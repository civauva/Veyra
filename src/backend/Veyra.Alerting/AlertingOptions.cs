using Veyra.Alerting.Catalog;
using Veyra.Alerting.ErrorRates;
using Veyra.Alerting.Incidents;
using Veyra.Alerting.MetricRules;
using Veyra.Alerting.Notifications;

namespace Veyra.Alerting;

/// <summary>
/// Settings of the Alerting module, bound from the <c>Veyra:Alerting</c> section.
/// </summary>
public sealed class AlertingOptions
{
    /// <summary>How often alert rules are evaluated against stored telemetry. Defaults to 30 seconds.</summary>
    public TimeSpan EvaluationInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Veyra's public address, for example <c>https://veyra.example.com</c>. Notifications link to the alert with it;
    /// without it they carry no link. Docker Compose sets it from <c>VEYRA_DOMAIN</c>.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// How many days resolved alerts are kept for the Alerts screen. Defaults to 90 (ADR 0012); open alerts are never
    /// dropped.
    /// </summary>
    public int HistoryDays { get; set; } = 90;

    /// <summary>The built-in error-rate rule, bound from <c>Veyra:Alerting:ErrorRate</c>.</summary>
    public ErrorRateOptions ErrorRate { get; set; } = new();

    /// <summary>The built-in disk-space rule, bound from <c>Veyra:Alerting:DiskSpace</c>.</summary>
    public DiskSpaceOptions DiskSpace { get; set; } = new();

    /// <summary>The built-in disk-filling rule, bound from <c>Veyra:Alerting:DiskFilling</c>.</summary>
    public DiskFillingOptions DiskFilling { get; set; } = new();

    /// <summary>The built-in latency rule, bound from <c>Veyra:Alerting:Latency</c>.</summary>
    public LatencyOptions Latency { get; set; } = new();

    /// <summary>The restart rule for Docker containers (ADR 0018).</summary>
    public ContainerRestartsOptions ContainerRestarts { get; set; } = new();

    /// <summary>The memory rule for Docker containers (ADR 0018).</summary>
    public ContainerMemoryOptions ContainerMemory { get; set; } = new();

    /// <summary>The blocking rule for database servers (ADR 0018).</summary>
    public DatabaseBlockingOptions DatabaseBlocking { get; set; } = new();

    /// <summary>The deadlock rule for database servers (ADR 0018).</summary>
    public DatabaseDeadlocksOptions DatabaseDeadlocks { get; set; } = new();

    /// <summary>The long-query rule for databases (ADR 0018).</summary>
    public DatabaseLongQueryOptions DatabaseLongQuery { get; set; } = new();

    /// <summary>The backup-age rule for databases (ADR 0018).</summary>
    public DatabaseBackupAgeOptions DatabaseBackupAge { get; set; } = new();

    /// <summary>The SQL Agent job rule (ADR 0018).</summary>
    public DatabaseAgentJobOptions DatabaseAgentJob { get; set; } = new();

    /// <summary>The connections rule for database servers that say their maximum (ADR 0018).</summary>
    public DatabaseConnectionsOptions DatabaseConnections { get; set; } = new();

    /// <summary>The transaction ID wraparound rule for PostgreSQL databases (ADR 0018).</summary>
    public DatabaseWraparoundOptions DatabaseWraparound { get; set; } = new();

    /// <summary>
    /// The catalog rules' settings, by each rule's section, such as <c>HeadBlocker</c> (ADR 0020). A rule left out
    /// runs with the catalog's defaults.
    /// </summary>
    public Dictionary<string, CatalogRuleOptions> Catalog { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Alert notifications, bound from <c>Veyra:Alerting:Notifications</c>.</summary>
    public NotificationOptions Notifications { get; set; } = new();

    /// <summary>How alerts are grouped into incidents (ADR 0026), bound from <c>Veyra:Alerting:Incidents</c>.</summary>
    public IncidentOptions Incidents { get; set; } = new();

    /// <summary>
    /// The installation's own runbook per rule, by the rule's name, such as <c>error-rate</c> (ADR 0026). An
    /// incident's View runbook opens it; a rule without one shows Veyra's built-in guidance instead.
    /// </summary>
    public Dictionary<string, string> Runbooks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The runbook link of a rule, when one is set.</summary>
    /// <param name="rule">The rule's name.</param>
    /// <returns>The link, or <see langword="null"/> when the rule has none.</returns>
    public Uri? RunbookOf(string rule) =>
        Runbooks.TryGetValue(rule, out var link) && !string.IsNullOrWhiteSpace(link)
            && Uri.TryCreate(link.Trim(), UriKind.Absolute, out var url)
            ? url
            : null;

    /// <summary>
    /// <see cref="PublicUrl"/> as an address that relative links resolve under, ending in a slash.
    /// </summary>
    /// <returns>The address, or <see langword="null"/> when none is set.</returns>
    public Uri? PublicBaseUri() =>
        string.IsNullOrWhiteSpace(PublicUrl)
            ? null
            : new Uri(PublicUrl.EndsWith('/') ? PublicUrl : PublicUrl + "/", UriKind.Absolute);
}
