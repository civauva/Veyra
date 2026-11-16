namespace Veyra.Core.Traces;

/// <summary>How much of a span was left out, by its sender or by Veyra's limits (ADR 0014).</summary>
/// <param name="Attributes">Attributes left out.</param>
/// <param name="Events">Events left out.</param>
/// <param name="Links">Links left out.</param>
public sealed record SpanDropped(int Attributes, int Events, int Links)
{
    /// <summary>Nothing left out.</summary>
    public static SpanDropped None { get; } = new(0, 0, 0);
}
