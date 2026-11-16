using System.Security.Cryptography;
using System.Text;

namespace Veyra.Agents.Enrollment;

/// <summary>
/// The one-time code an installer exchanges for an agent's key, such as <c>K7QM-4TWX-9PRA</c>. People read it off the
/// screen and may type it, so it has no letters that look alike and no case.
/// </summary>
internal static class EnrollmentCode
{
    // 32 characters without 0, O, 1, and I: each one is 5 random bits, 60 in a code. With a one-hour life, one use,
    // and failed tries throttled, guessing one is out of reach.
    private const string _alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int _groups = 3;

    private const int _groupLength = 4;

    /// <summary>Makes a new code with the system's cryptographic generator.</summary>
    /// <returns>Three groups of four characters, joined by dashes.</returns>
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(_groups * _groupLength);
        var characters = bytes.Select(value => _alphabet[value % _alphabet.Length]);

        return Format(string.Concat(characters));
    }

    /// <summary>Reads a code as someone typed or pasted it: any case, with or without dashes and spaces.</summary>
    /// <param name="typed">The code as given.</param>
    /// <returns>The code as issued, or <see langword="null"/> when it cannot be one.</returns>
    public static string? Normalize(string? typed)
    {
        var plain = string.Concat((typed ?? "").Where(c => c is not ('-' or ' '))).ToUpperInvariant();

        return plain.Length == _groups * _groupLength && plain.All(_alphabet.Contains) ? Format(plain) : null;
    }

    /// <summary>The hash a code is stored as.</summary>
    /// <param name="code">The code as issued.</param>
    /// <returns>Its SHA-256.</returns>
    public static byte[] Hash(string code) => SHA256.HashData(Encoding.UTF8.GetBytes(code));

    private static string Format(string plain) =>
        string.Join(
            '-',
            Enumerable.Range(0, _groups).Select(group => plain.Substring(group * _groupLength, _groupLength)));
}
