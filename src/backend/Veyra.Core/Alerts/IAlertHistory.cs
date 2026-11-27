namespace Veyra.Core.Alerts;

/// <summary>
/// Reads the history of alerts as stretches at one severity. The Alerting module implements it; the Query module
/// works out the Overview's availability from it (ADR 0023).
/// </summary>
public interface IAlertHistory
{
    /// <summary>Lists every stretch of every alert that overlaps a period.</summary>
    /// <param name="periodStart">The start of the period.</param>
    /// <param name="periodEnd">The end of the period.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stretches, in no particular order; they may reach outside the period.</returns>
    Task<IReadOnlyList<AlertSpell>> ListSpellsAsync(
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        CancellationToken cancellationToken);
}
