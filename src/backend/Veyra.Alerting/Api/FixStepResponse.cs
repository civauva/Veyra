namespace Veyra.Alerting.Api;

/// <summary>One thing a person does to resolve a catalog rule's alert, in order with the others (VEY-60).</summary>
/// <param name="Text">What to do and why, with <c>{server}</c> and <c>{part}</c> to fill in.</param>
/// <param name="Script">
/// A query or command to run, with the same placeholders, or null. In a script, <c>{part}</c> is filled in so that it
/// is safe inside <c>N'…'</c>, and <c>[{part}]</c> as a bracketed name.
/// </param>
public sealed record FixStepResponse(string Text, string? Script);
