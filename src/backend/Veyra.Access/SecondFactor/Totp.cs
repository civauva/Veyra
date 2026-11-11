using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Veyra.Access.SecondFactor;

/// <summary>
/// Time-based one-time passwords (RFC 6238) as authenticator apps make them: six digits, a new one every 30 seconds,
/// HMAC-SHA1 (ADR 0011).
/// </summary>
/// <remarks>
/// SHA-1 is what every authenticator app supports for this. It is used here as an HMAC, where its collision weakness
/// does not matter.
/// </remarks>
internal static class Totp
{
    /// <summary>How long one code is valid.</summary>
    public static readonly TimeSpan StepLength = TimeSpan.FromSeconds(30);

    private const int _digits = 6;

    private const int _secretBytes = 20;

    /// <summary>Makes a new random 160-bit secret, the size RFC 4226 recommends.</summary>
    /// <returns>The secret.</returns>
    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(_secretBytes);

    /// <summary>The number of the 30-second step a time falls into.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The step since the Unix epoch.</returns>
    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / (long)StepLength.TotalSeconds;

    /// <summary>The code of one step.</summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="step">The step.</param>
    /// <returns>Six digits, with leading zeros.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 with HMAC-SHA1 is what authenticator apps support; ADR 0011.")]
    public static string Code(ReadOnlySpan<byte> secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(secret, counter, hash);

        // Dynamic truncation, RFC 4226 section 5.3.
        var offset = hash[^1] & 0x0F;
        var value = (BinaryPrimitives.ReadInt32BigEndian(hash[offset..]) & 0x7FFF_FFFF) % 1_000_000;

        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Checks a typed code against the current step and one step on either side, for clocks that drift.
    /// </summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="typed">The code as typed; spaces and dashes are ignored.</param>
    /// <param name="now">The current time.</param>
    /// <param name="lastUsedStep">The step of the last accepted code; it and earlier steps are refused.</param>
    /// <returns>The step the code belongs to, or <see langword="null"/> when it is wrong or already used.</returns>
    public static long? Verify(ReadOnlySpan<byte> secret, string typed, DateTimeOffset now, long? lastUsedStep)
    {
        var code = Clean(typed);

        if (code is null)
        {
            return null;
        }

        var current = StepAt(now);

        for (var step = current - 1; step <= current + 1; step++)
        {
            if (step <= lastUsedStep)
            {
                continue;
            }

            // Constant time, so how fast a guess is refused says nothing about how close it was.
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(Code(secret, step)),
                Encoding.ASCII.GetBytes(code)))
            {
                return step;
            }
        }

        return null;
    }

    /// <summary>The <c>otpauth://</c> address that authenticator apps read from the QR code.</summary>
    /// <param name="secret">The shared secret.</param>
    /// <param name="account">The account name the app shows, the email address.</param>
    /// <returns>The address.</returns>
    public static string Uri(byte[] secret, string account) =>
        $"otpauth://totp/Veyra:{System.Uri.EscapeDataString(account)}?secret={Base32.Encode(secret)}&issuer=Veyra"
        + $"&algorithm=SHA1&digits={_digits}&period={(int)StepLength.TotalSeconds}";

    private static string? Clean(string typed)
    {
        var digits = new StringBuilder(_digits);

        foreach (var character in typed)
        {
            if (character is ' ' or '-')
            {
                continue;
            }

            if (!char.IsAsciiDigit(character))
            {
                return null;
            }

            digits.Append(character);
        }

        return digits.Length == _digits ? digits.ToString() : null;
    }
}
