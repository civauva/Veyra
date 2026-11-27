using Veyra.Core.Alerts;
using Veyra.Query.Home;

namespace Veyra.Query.Resources;

/// <summary>
/// The health of the resource lists' items, from the open alerts only (ADR 0007): an item is lit only when an alert
/// explains it. Pure; tested without a database.
/// </summary>
/// <param name="alerts">The alerts; resolved ones are left out.</param>
internal sealed class ResourceHealth(IReadOnlyList<AlertState> alerts)
{
    private readonly ILookup<(AlertBlockKind Kind, string Name), AlertState> _open = alerts
        .Where(alert => alert.ResolvedAt is null)
        .ToLookup(alert => (alert.Subject.Kind, alert.Subject.Name));

    /// <summary>The health of one block.</summary>
    /// <param name="kind">The block's kind.</param>
    /// <param name="name">
    /// The block's name: a service, host, or container name, or for a database server its engine and server.
    /// </param>
    /// <param name="environment">
    /// The environment of a service; null for a block of every environment, which any alert of its name lights.
    /// </param>
    /// <param name="host">For a container, its host, which its alerts name in their part (ADR 0018).</param>
    /// <returns>Warning or Caution when such an alert is open; Ok otherwise.</returns>
    public BlockHealth Of(AlertBlockKind kind, string name, string? environment, string? host = null)
    {
        var lit = _open[(kind, name)]
            .Where(alert => environment is null || alert.Environment is null || alert.Environment == environment)
            .Where(alert => kind != AlertBlockKind.Container
                || alert.Subject.Part is null
                || alert.Subject.Part == host)
            .ToList();

        if (lit.Count == 0)
        {
            return BlockHealth.Ok;
        }

        return lit.Max(alert => alert.Severity) == AlertSeverity.Warning ? BlockHealth.Warning : BlockHealth.Caution;
    }
}
