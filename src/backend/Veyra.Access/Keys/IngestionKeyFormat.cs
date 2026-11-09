using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Veyra.Access.Keys;

/// <summary>
/// Creates and recognises ingestion keys: <c>vyk_</c> followed by 32 random bytes in unpadded base64url (ADR 0008).
/// </summary>
/// <remarks>
/// The fixed prefix lets secret scanners find leaked keys. The random part has 256 bits, so a key cannot be guessed,
/// and a single fast SHA-256 hash is enough to store it safely.
/// </remarks>
internal static class IngestionKeyFormat
{
    /// <summary>The prefix every ingestion key starts with.</summary>
    public const string Prefix = "vyk_";

    private const int _randomBytes = 32;

    // 32 bytes in unpadded base64url are always 43 characters.
    private const int _randomCharacters = 43;

    private const int _displayCharacters = 8;

    /// <summary>
    /// Creates a new random key.
    /// </summary>
    /// <returns>The full key. It is shown to the person once and never stored.</returns>
    public static string Generate() =>
        Prefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(_randomBytes));

    /// <summary>
    /// Hashes a key for storage and lookup.
    /// </summary>
    /// <param name="key">The full key.</param>
    /// <returns>The SHA-256 hash of the key's UTF-8 bytes.</returns>
    public static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    /// <summary>
    /// Returns the part of a key that is safe to show in lists, so people can tell keys apart.
    /// </summary>
    /// <param name="key">The full key.</param>
    /// <returns>
    /// The prefix and the first eight characters of the random part, for example <c>vyk_AbCdEfGh</c>.
    /// </returns>
    public static string DisplayPrefix(string key) => key[..(Prefix.Length + _displayCharacters)];

    /// <summary>
    /// Checks the shape of a presented key before any database work, so obvious garbage costs nothing.
    /// </summary>
    /// <param name="candidate">The value a sender presented.</param>
    /// <returns><see langword="true"/> when it has the prefix and 43 base64url characters.</returns>
    public static bool LooksLikeKey(string candidate)
    {
        if (candidate.Length != Prefix.Length + _randomCharacters
            || !candidate.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var character in candidate.AsSpan(Prefix.Length))
        {
            var isBase64Url = char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

            if (!isBase64Url)
            {
                return false;
            }
        }

        return true;
    }
}
