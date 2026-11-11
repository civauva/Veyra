using System.Security.Cryptography;
using System.Text;

namespace Veyra.Access.SecondFactor;

/// <summary>
/// Single-use codes that stand in for the authenticator app when it is lost (ADR 0011). Each is eight characters,
/// shown as two groups of four, from an alphabet without the look-alikes 0, O, 1, and I: 40 random bits.
/// </summary>
internal static class RecoveryCodes
{
    /// <summary>How many codes a set has.</summary>
    public const int Count = 10;

    private const string _alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int _length = 8;

    /// <summary>Makes a new set of distinct codes.</summary>
    /// <returns>The codes, as shown to the person.</returns>
    public static IReadOnlyList<string> Generate()
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);

        while (codes.Count < Count)
        {
            var code = RandomNumberGenerator.GetString(_alphabet, _length);
            codes.Add($"{code[..4]}-{code[4..]}");
        }

        return [.. codes];
    }

    /// <summary>
    /// The stored form of a code: SHA-256 of the code without spaces and dashes, in upper case. The codes are random,
    /// so a slow password hash would add nothing.
    /// </summary>
    /// <param name="typed">The code as typed or as generated.</param>
    /// <returns>The hash.</returns>
    public static byte[] Hash(string typed) => SHA256.HashData(Encoding.ASCII.GetBytes(Normalize(typed)));

    /// <summary>Whether typed text is shaped like a recovery code rather than a six-digit app code.</summary>
    /// <param name="typed">The text as typed.</param>
    /// <returns><see langword="true"/> for eight characters from the alphabet.</returns>
    public static bool LooksLikeOne(string typed)
    {
        var code = Normalize(typed);

        return code.Length == _length && code.All(character => _alphabet.Contains(character, StringComparison.Ordinal));
    }

    private static string Normalize(string typed) =>
        new([.. typed.Where(character => character is not (' ' or '-')).Select(char.ToUpperInvariant)]);
}
