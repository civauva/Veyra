using Veyra.Query.Home;

namespace Veyra.Query.Resources;

/// <summary>
/// The health chips of a resource list (ADR 0022): <c>ok</c>, <c>caution</c>, <c>warning</c>, <c>unknown</c>, and
/// <c>silent</c>. An item is kept when it matches any chip chosen; without chips, every item is.
/// </summary>
internal sealed class ResourceHealthFilter
{
    private const string _silent = "silent";

    private readonly HashSet<BlockHealth> _healths;

    private readonly bool _silentChosen;

    private ResourceHealthFilter(HashSet<BlockHealth> healths, bool silent)
    {
        _healths = healths;
        _silentChosen = silent;
    }

    /// <summary>Reads the chips, given as repeated parameters, separated by commas, or both.</summary>
    /// <param name="values">The parameter's values; null or empty for every item.</param>
    /// <returns>The filter, or null when a value is not a chip.</returns>
    public static ResourceHealthFilter? Parse(IReadOnlyList<string>? values)
    {
        var healths = new HashSet<BlockHealth>();
        var silent = false;

        foreach (var value in (values ?? []).SelectMany(value => value.Split(',')))
        {
            var chip = value.Trim();

            if (chip.Length == 0)
            {
                continue;
            }

            if (string.Equals(chip, _silent, StringComparison.OrdinalIgnoreCase))
            {
                silent = true;
            }
            else if (Enum.TryParse<BlockHealth>(chip, ignoreCase: true, out var health)
                && Enum.IsDefined(health)
                && !int.TryParse(chip, out _))
            {
                healths.Add(health);
            }
            else
            {
                return null;
            }
        }

        return new ResourceHealthFilter(healths, silent);
    }

    /// <summary>Whether an item stays in the list.</summary>
    /// <param name="health">Its health from its open alerts.</param>
    /// <param name="silent">Whether it is silent.</param>
    /// <returns><see langword="true"/> when it matches a chip, or when no chip is chosen.</returns>
    public bool Keeps(BlockHealth health, bool silent) =>
        (_healths.Count == 0 && !_silentChosen) || _healths.Contains(health) || (_silentChosen && silent);
}
