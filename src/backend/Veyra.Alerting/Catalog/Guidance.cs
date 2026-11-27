namespace Veyra.Alerting.Catalog;

/// <summary>
/// What a rule's alert means and how to resolve it (VEY-60), so its details tell a person what to do, not only what
/// was measured.
/// </summary>
/// <param name="Meaning">
/// What is wrong and why it matters, in a sentence or two, with <c>{server}</c> and <c>{part}</c> to fill in.
/// </param>
/// <param name="Steps">What to do, in order.</param>
internal sealed record Guidance(string Meaning, IReadOnlyList<FixStep> Steps);
