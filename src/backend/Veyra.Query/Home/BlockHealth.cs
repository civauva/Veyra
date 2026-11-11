namespace Veyra.Query.Home;

/// <summary>
/// The state of one block. Set only by alerts, so a lit block always has an alert that explains it (ADR 0007).
/// </summary>
public enum BlockHealth
{
    /// <summary>No alert is open for the block.</summary>
    Ok,

    /// <summary>A caution is open: something needs attention, users are not affected yet.</summary>
    Caution,

    /// <summary>A warning is open: users are affected now.</summary>
    Warning,

    /// <summary>No data arrived within the expected interval. Not set yet in this version.</summary>
    Unknown,
}
