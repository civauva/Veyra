namespace Veyra.Core.Settings;

/// <summary>One change to a monitored system, as the settings history kept it.</summary>
/// <param name="At">When it was made.</param>
/// <param name="By">The name of who made it, or <see langword="null"/> for Veyra itself.</param>
/// <param name="Kind">Whether the system was added, changed, or removed.</param>
/// <param name="Fields">The fields a change touched.</param>
/// <param name="From">The value before, for a change that keeps one, such as a template version.</param>
/// <param name="To">The value after, for a change that keeps one.</param>
public sealed record MonitoringHistoryEntry(
    DateTimeOffset At,
    string? By,
    MonitoringChangeKind Kind,
    IReadOnlyList<string> Fields,
    string? From,
    string? To);
