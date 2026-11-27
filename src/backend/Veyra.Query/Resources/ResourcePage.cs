using System.Globalization;
using System.Text;

namespace Veyra.Query.Resources;

/// <summary>
/// One page of a sorted resource list, and the cursor of the next (ADR 0022). The lists are small enough to sort
/// whole, since the header counts the whole set anyway, so the cursor is the position the next page starts at.
/// </summary>
/// <typeparam name="T">The item.</typeparam>
/// <param name="Items">The page's items.</param>
/// <param name="Total">How many items the whole list has.</param>
/// <param name="Next">The cursor of the next page; null on the last page.</param>
internal sealed record ResourcePage<T>(IReadOnlyList<T> Items, int Total, string? Next);

/// <summary>Cuts a sorted list into pages.</summary>
internal static class ResourcePage
{
    private const string _prefix = "r1:";

    /// <summary>Takes the page a cursor points at.</summary>
    /// <typeparam name="T">The item.</typeparam>
    /// <param name="items">The whole list, sorted.</param>
    /// <param name="cursor">The cursor of the previous page; null for the first page.</param>
    /// <param name="limit">The most items on a page.</param>
    /// <returns>The page, or null when the cursor is not one Veyra issued.</returns>
    public static ResourcePage<T>? Of<T>(IReadOnlyList<T> items, string? cursor, int limit)
    {
        ArgumentNullException.ThrowIfNull(items);

        var offset = 0;

        if (!string.IsNullOrEmpty(cursor) && !TryDecode(cursor, out offset))
        {
            return null;
        }

        var page = items.Skip(offset).Take(limit).ToList();
        var next = offset + page.Count < items.Count ? Encode(offset + page.Count) : null;

        return new ResourcePage<T>(page, items.Count, next);
    }

    private static string Encode(int offset) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(_prefix + offset.ToString(CultureInfo.InvariantCulture)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool TryDecode(string cursor, out int offset)
    {
        offset = 0;

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var text = Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            return text.StartsWith(_prefix, StringComparison.Ordinal)
                && int.TryParse(text[_prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out offset);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
