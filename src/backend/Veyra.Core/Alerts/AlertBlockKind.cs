namespace Veyra.Core.Alerts;

/// <summary>
/// The kind of block an alert is about (ADR 0007, ADR 0012). Stored as text in <c>alerting.alert.block_kind</c>.
/// </summary>
public enum AlertBlockKind
{
    /// <summary>A service, named by its <c>service.name</c>.</summary>
    Service = 1,

    /// <summary>A host, named by its <c>host.name</c>.</summary>
    Host = 2,
}
