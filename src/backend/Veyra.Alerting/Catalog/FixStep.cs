namespace Veyra.Alerting.Catalog;

/// <summary>One thing a person does to resolve an alert, in order with the others (VEY-60).</summary>
/// <param name="Text">
/// What to do and why, with <c>{server}</c> and <c>{part}</c> for screens to fill in, such as the database's name.
/// </param>
/// <param name="Script">
/// A query or command to run, with the same placeholders, or <see langword="null"/>. Screens fill <c>{part}</c> in a
/// script so that it is safe inside <c>N'…'</c>, and <c>[{part}]</c> as a bracketed name.
/// </param>
internal sealed record FixStep(string Text, string? Script = null);
