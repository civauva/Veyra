using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Veyra.Core.Traces;

/// <summary>
/// Where the next page of the trace list in <see cref="TraceOrder.Worst"/> order starts: after this root span (keyset
/// paging, ADR 0029).
/// </summary>
/// <param name="Failed">Whether the last root span of the page failed.</param>
/// <param name="Duration">Its duration, kept to the microsecond as stored.</param>
/// <param name="TraceId">Its trace ID in lower-case hex.</param>
/// <param name="SpanId">
/// Its span ID in lower-case hex, which orders the rare second root of one trace with the same duration.
/// </param>
public sealed record TraceWorstCursor(bool Failed, TimeSpan Duration, string TraceId, string SpanId)
{
    // Marks the token as of this order, so a token of the newest order is never read as one of this order.
    private const string _prefix = "w";

    /// <summary>Encodes the cursor as an opaque, URL-safe token.</summary>
    /// <returns>The token.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"{_prefix}:{(Failed ? 1 : 0)}:{(long)Duration.TotalMicroseconds}:{TraceId}:{SpanId}")));

    /// <summary>Decodes a token made by <see cref="Encode"/>.</summary>
    /// <param name="token">The token from the client.</param>
    /// <param name="cursor">The cursor, when the token is valid.</param>
    /// <returns>Whether the token is valid.</returns>
    public static bool TryDecode(string token, out TraceWorstCursor? cursor)
    {
        cursor = null;

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)).Split(':');

            if (parts.Length == 5
                && parts[0] == _prefix
                && parts[1] is "0" or "1"
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var microseconds)
                && microseconds <= TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMicrosecond
                && IsHex(parts[3], 32)
                && IsHex(parts[4], 16))
            {
                cursor = new TraceWorstCursor(
                    parts[1] == "1",
                    TimeSpan.FromMicroseconds(microseconds),
                    parts[3],
                    parts[4]);

                return true;
            }
        }
        catch (FormatException)
        {
            // Not base64url: treated like any other invalid token.
        }

        return false;
    }

    private static bool IsHex(string text, int length) => text.Length == length && text.All(char.IsAsciiHexDigitLower);
}
