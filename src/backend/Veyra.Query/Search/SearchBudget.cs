namespace Veyra.Query.Search;

/// <summary>How long each group of a search may take before it answers with what it has (ADR 0022).</summary>
/// <param name="PerGroup">The time each group gets, 150 ms in production; tests make it shorter.</param>
internal sealed record SearchBudget(TimeSpan PerGroup)
{
    /// <summary>The budget of ADR 0022.</summary>
    public static SearchBudget Default { get; } = new(TimeSpan.FromMilliseconds(150));
}
