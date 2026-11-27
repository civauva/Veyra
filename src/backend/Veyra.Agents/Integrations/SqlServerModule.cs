namespace Veyra.Agents.Integrations;

/// <summary>
/// One module of the SQL Server template's reads (ADR 0028): the receivers it is made of, its default interval, and
/// the intervals it may be given.
/// </summary>
/// <param name="Id">Its ID, such as <c>activity</c>, stored per server.</param>
/// <param name="Name">Its name as the wizard shows it.</param>
/// <param name="Description">What it reads, in a sentence.</param>
/// <param name="DefaultIntervalSeconds">
/// The interval of its first receiver in the template. Its other receivers keep their ratio to it.
/// </param>
/// <param name="Choices">The intervals it may be given, in seconds, ascending, the default among them.</param>
/// <param name="Required">Whether it can be switched off; Core cannot.</param>
/// <param name="Receivers">The template's receivers it is made of, such as <c>sqlquery/live</c>.</param>
internal sealed record SqlServerModule(
    string Id,
    string Name,
    string Description,
    int DefaultIntervalSeconds,
    IReadOnlyList<int> Choices,
    bool Required,
    IReadOnlyList<string> Receivers);
