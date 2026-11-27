namespace Veyra.Query.Streams;

/// <summary>
/// The words of a text, for matching a live record against a word search without the database.
/// </summary>
/// <remarks>
/// A word is a run of letters and digits, in lower case, which is what PostgreSQL's <c>simple</c> parser makes of
/// ordinary messages (ADR 0016). It keeps a few more shapes whole, such as e-mail addresses and paths, as well as their
/// parts; a search for such a shape can find a stored record that the live stream did not show, never the reverse for
/// ordinary words.
/// </remarks>
internal static class LiveWords
{
    /// <summary>Splits a text into its distinct words.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The words, in lower case.</returns>
    public static HashSet<string> Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var words = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;

        for (var index = 0; index <= text.Length; index++)
        {
            var inWord = index < text.Length && char.IsLetterOrDigit(text[index]);

            if (inWord && start < 0)
            {
                start = index;
            }
            else if (!inWord && start >= 0)
            {
                words.Add(text[start..index].ToLowerInvariant());
                start = -1;
            }
        }

        return words;
    }
}
