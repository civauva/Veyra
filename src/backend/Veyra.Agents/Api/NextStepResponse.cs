namespace Veyra.Agents.Api;

/// <summary>
/// One step the installation still lacks, or has done, for the integration page's checklist (ADR 0028).
/// </summary>
/// <param name="Id">The step's ID: <c>thresholds</c>, <c>channel</c>, or <c>retention</c>.</param>
/// <param name="Text">What to do, in a sentence.</param>
/// <param name="Done">Whether it is done.</param>
/// <param name="Link">The screen that does it, as a path in the web app.</param>
public sealed record NextStepResponse(string Id, string Text, bool Done, string? Link);
