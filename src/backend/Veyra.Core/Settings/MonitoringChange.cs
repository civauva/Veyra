namespace Veyra.Core.Settings;

/// <summary>
/// One change to a monitored system or agent, for the settings history. It names the fields that changed and never
/// their values, so a password or a connection detail does not end up in the history (ADR 0019).
/// </summary>
/// <param name="Subject">What kind of system it is.</param>
/// <param name="Name">Its name, as the Settings screen shows it.</param>
/// <param name="Kind">Whether it was added, changed, or removed.</param>
/// <param name="Fields">For a change, the fields it touched, in words such as <c>port</c> or <c>password</c>.</param>
public sealed record MonitoringChange(
    MonitoringSubject Subject,
    string Name,
    MonitoringChangeKind Kind,
    IReadOnlyList<string> Fields);
