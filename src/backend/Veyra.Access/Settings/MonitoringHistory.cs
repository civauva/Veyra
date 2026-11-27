using System.Security.Claims;
using Veyra.Access.Sessions;
using Veyra.Access.Users;
using Veyra.Core.Settings;

namespace Veyra.Access.Settings;

/// <summary>
/// Keeps the changes other modules make to monitored systems and agents in the settings history, named after the
/// person who made them, as ADR 0019 asks.
/// </summary>
/// <param name="store">Where the history is kept.</param>
/// <param name="users">Finds the person's name, which the history keeps after the account is removed.</param>
internal sealed class MonitoringHistory(SettingStore store, UserStore users) : IMonitoringHistory
{
    /// <inheritdoc />
    public async Task RecordAsync(MonitoringChange change, ClaimsPrincipal by, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(by);

        var userId = by.UserId();
        var user = await users.FindByIdAsync(userId, cancellationToken);

        await store.RecordMonitoringAsync(
            change,
            new SettingAuthor(userId, user?.DisplayName ?? "Unknown"),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MonitoringHistoryEntry>> ListAsync(
        MonitoringSubject subject,
        string name,
        int limit,
        CancellationToken cancellationToken)
    {
        var changes = await store.MonitoringHistoryAsync(subject, name, limit, cancellationToken);

        return changes
            .Select(change => new MonitoringHistoryEntry(
                change.ChangedAt,
                change.ChangedByName,
                change.Kind switch
                {
                    SettingChangeKind.Added => MonitoringChangeKind.Added,
                    SettingChangeKind.Removed => MonitoringChangeKind.Removed,
                    _ => MonitoringChangeKind.Changed,
                },
                change.Fields ?? [],
                change.OldValue,
                change.NewValue))
            .ToList();
    }
}
