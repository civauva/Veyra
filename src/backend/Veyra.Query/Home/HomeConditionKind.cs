namespace Veyra.Query.Home;

/// <summary>
/// The weather an environment is in, as the Forecast home shows it (ADR 0007).
/// </summary>
public enum HomeConditionKind
{
    /// <summary>No alert is open.</summary>
    Clear,

    /// <summary>Only cautions are open.</summary>
    Showers,

    /// <summary>At least one warning is open.</summary>
    Storm,
}
