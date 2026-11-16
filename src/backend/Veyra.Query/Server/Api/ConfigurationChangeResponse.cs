namespace Veyra.Query.Server.Api;

/// <summary>A change of a setting or of the sysadmin members.</summary>
/// <param name="At">When the agent first saw it.</param>
/// <param name="Item">The setting, or <c>sysadmin</c>.</param>
/// <param name="From">The value before, or empty for a new member.</param>
/// <param name="To">The value after, or the new member.</param>
public sealed record ConfigurationChangeResponse(DateTimeOffset At, string Item, string From, string To);
