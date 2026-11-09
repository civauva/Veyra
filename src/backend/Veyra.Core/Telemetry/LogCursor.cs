using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Veyra.Core.Telemetry;

/// <summary>
/// Marks where a page of log records ended, so the next page continues right after it (keyset paging).
/// </summary>
/// <remarks>
/// Keyset paging stays fast however deep a person scrolls and never shows a record twice, unlike offsets, which get
/// slower with depth and shift when new records arrive.
/// </remarks>
/// <param name="Timestamp">The timestamp of the last record on the page.</param>
/// <param name="Id">The ID of the last record on the page, which breaks ties between equal timestamps.</param>
public sealed record LogCursor(DateTimeOffset Timestamp, long Id)
{
    /// <summary>
    /// Turns the cursor into an opaque, URL-safe token for API clients.
    /// </summary>
    /// <returns>The token.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{Timestamp.UtcTicks}:{Id}")));

    /// <summary>
    /// Reads a token made by <see cref="Encode"/>.
    /// </summary>
    /// <param name="token">The token from the API client.</param>
    /// <param name="cursor">The cursor, when the token is valid.</param>
    /// <returns><see langword="true"/> when the token is valid.</returns>
    public static bool TryDecode(string token, out LogCursor? cursor)
    {
        cursor = null;

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)).Split(':');

            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && ticks is >= 0 and <= 3155378975999999999)
            {
                cursor = new LogCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);

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
