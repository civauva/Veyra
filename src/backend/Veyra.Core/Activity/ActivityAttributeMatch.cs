namespace Veyra.Core.Activity;

/// <summary>
/// Which records to read by one of their attributes, so storage leaves the others out before its limit (VEY-68).
/// </summary>
/// <param name="Attribute">The attribute, such as <c>event_name</c>.</param>
/// <param name="Values">Values the attribute may equal.</param>
/// <param name="Containing">Text the attribute may contain instead.</param>
public sealed record ActivityAttributeMatch(
    string Attribute,
    IReadOnlyCollection<string> Values,
    IReadOnlyCollection<string> Containing)
{
    /// <summary>Whether a record's attributes match, the same way storage decides. Pure.</summary>
    /// <param name="attributes">The record's attributes.</param>
    /// <returns><see langword="true"/> when the attribute equals a value or contains a text.</returns>
    public bool Matches(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        if (!attributes.TryGetValue(Attribute, out var value))
        {
            return false;
        }

        return Values.Contains(value, StringComparer.Ordinal)
            || Containing.Any(part => value.Contains(part, StringComparison.Ordinal));
    }
}
