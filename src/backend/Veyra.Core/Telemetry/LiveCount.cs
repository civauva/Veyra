namespace Veyra.Core.Telemetry;

/// <summary>How many spans or metric points of one environment a written batch held, for the live rates.</summary>
/// <param name="Environment">The environment, or <see langword="null"/> when the sender set none.</param>
/// <param name="Count">How many.</param>
public sealed record LiveCount(string? Environment, int Count)
{
    /// <summary>Counts the items of a batch per environment.</summary>
    /// <typeparam name="T">The kind of item, a span or a metric point.</typeparam>
    /// <param name="items">The batch.</param>
    /// <param name="environment">Reads an item's environment.</param>
    /// <returns>One count per environment in the batch, in the order the environments first appear.</returns>
    public static IReadOnlyList<LiveCount> Of<T>(IEnumerable<T> items, Func<T, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(environment);

        var counts = new List<LiveCount>();

        // Batches nearly always hold one environment, so a short list beats a dictionary.
        foreach (var item in items)
        {
            var name = environment(item);
            var index = counts.FindIndex(count => count.Environment == name);

            if (index < 0)
            {
                counts.Add(new LiveCount(name, 1));
            }
            else
            {
                counts[index] = counts[index] with { Count = counts[index].Count + 1 };
            }
        }

        return counts;
    }
}
