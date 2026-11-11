using System.Text;

namespace Veyra.Access.SecondFactor;

/// <summary>
/// Base32 of RFC 4648, the form authenticator apps expect a secret in. Written without padding, read with or without
/// it, ignoring case and spaces, as people type it.
/// </summary>
internal static class Base32
{
    private const string _alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Encodes bytes without padding.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The text.</returns>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        var text = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;

        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bits += 8;

            while (bits >= 5)
            {
                text.Append(_alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            text.Append(_alphabet[(buffer << (5 - bits)) & 31]);
        }

        return text.ToString();
    }

    /// <summary>Decodes text, ignoring case, spaces, and padding.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="FormatException">The text has a character outside the alphabet.</exception>
    public static byte[] Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = new List<byte>(text.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var character in text)
        {
            if (character is ' ' or '=')
            {
                continue;
            }

            var index = _alphabet.IndexOf(char.ToUpperInvariant(character), StringComparison.Ordinal);

            if (index < 0)
            {
                throw new FormatException($"'{character}' is not a Base32 character.");
            }

            buffer = (buffer << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
