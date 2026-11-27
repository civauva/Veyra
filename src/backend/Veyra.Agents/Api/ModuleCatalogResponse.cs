namespace Veyra.Agents.Api;

/// <summary>One module of the SQL Server template's reads, as the setup wizard offers it (ADR 0028).</summary>
/// <param name="Id">Its ID, such as <c>activity</c>.</param>
/// <param name="Name">Its name.</param>
/// <param name="Description">What it reads.</param>
/// <param name="DefaultIntervalSeconds">How often it reads unless an Admin chooses otherwise.</param>
/// <param name="Choices">The intervals it may be given, in seconds, ascending.</param>
/// <param name="Required">Whether it is always on; Core is.</param>
public sealed record ModuleCatalogResponse(
    string Id,
    string Name,
    string Description,
    int DefaultIntervalSeconds,
    IReadOnlyList<int> Choices,
    bool Required);
