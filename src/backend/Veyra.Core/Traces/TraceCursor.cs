using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Veyra.Core.Traces;

/// <summary>Where the next page of the trace list starts: after this root span (keyset paging, ADR 0014).</summary>
/// <param name="Start">The start of the last root span of the page.</param>
/// <param name="SpanId">Its span ID in lower-case hex, which orders roots that started at the same instant.</param>
public sealed record TraceCursor(DateTimeOffset Start, string SpanId)
{
    /// <summary>Encodes the cursor as an opaque, URL-safe token.</summary>
    /// <returns>The token.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{Start.UtcTicks}:{SpanId}")));

    /// <summary>Decodes a token made by <see cref="Encode"/>.</summary>
    /// <param name="token">The token from the client.</param>
    /// <param name="cursor">The cursor, when the token is valid.</param>
    /// <returns>Whether the token is valid.</returns>
    public static bool TryDecode(string token, out TraceCursor? cursor)
    {
        cursor = null;

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)).Split(':');

            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is >= 0 and <= 3155378975999999999
                && parts[1].Length == 16
                && parts[1].All(char.IsAsciiHexDigitLower))
            {
                cursor = new TraceCursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);

                return true;
            }
        }
        catch (FormatException)
        {
            // Not base64url: treated like any other invalid token.
        }

        return false;
    }
}
