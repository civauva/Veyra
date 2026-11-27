namespace Veyra.Agents.Api;

/// <summary>One module of an integration with what its reads sent over the window (ADR 0028).</summary>
/// <param name="Id">The module's ID.</param>
/// <param name="Name">Its name.</param>
/// <param name="Enabled">Whether it is switched on.</param>
/// <param name="IntervalSeconds">How often it reads.</param>
/// <param name="Series">How many metric series its reads sent in the window.</param>
/// <param name="Points">How many metric points they sent.</param>
/// <param name="Line">Points per bucket, oldest first; <see langword="null"/> for a bucket with none.</param>
public sealed record ModuleFiguresResponse(
    string Id,
    string Name,
    bool Enabled,
    int IntervalSeconds,
    int Series,
    long Points,
    IReadOnlyList<double?> Line);
