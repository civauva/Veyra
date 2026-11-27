namespace Veyra.Agents.Api;

/// <summary>
/// One of an integration's active reads, with the time SQL Server spent on it, from the statement statistics marked
/// <c>veyra-agent: name</c> (ADR 0028).
/// </summary>
/// <param name="Name">The read's name, such as <c>sessions</c>.</param>
/// <param name="Module">Its module.</param>
/// <param name="SecondsPerRun">
/// SQL Server's time per run over the window; <see langword="null"/> when the statement statistics did not list it,
/// as for a read too light to rank among a busy server's 100 heaviest.
/// </param>
/// <param name="Runs">How often it ran over the window, when listed.</param>
/// <param name="Ok">False while its receiver reports errors.</param>
public sealed record CheckResponse(string Name, string Module, double? SecondsPerRun, long? Runs, bool Ok);
