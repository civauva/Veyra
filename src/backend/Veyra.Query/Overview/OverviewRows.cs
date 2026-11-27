namespace Veyra.Query.Overview;

/// <summary>
/// How many rows the Overview's database and pipeline tables ask for. The screen asks for as many as fit in their
/// tiles, so a large screen shows more and a small one no fewer than the defaults (VEY-81).
/// </summary>
/// <param name="Databases">The most databases to list.</param>
/// <param name="Pipelines">The most pipelines to list.</param>
internal sealed record OverviewRows(int Databases, int Pipelines)
{
    /// <summary>The most rows a table may ask for: a tall screen's worth, and a bound on the answer's size.</summary>
    public const int Most = 50;

    /// <summary>The rows when the screen asks for none: what fits a laptop.</summary>
    public static OverviewRows Default { get; } = new(OverviewDatabases.MaxItems, OverviewJobs.MaxPipelines);
}
