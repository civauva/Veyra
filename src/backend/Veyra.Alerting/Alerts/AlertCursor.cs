using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Veyra.Alerting.Alerts;

/// <summary>
/// Marks where a page of alerts ended, so the next page continues right after it (keyset paging, as for logs).
/// </summary>
/// <param name="OpenedAt">When the last alert on the page opened.</param>
/// <param name="Id">The ID of the last alert on the page, which breaks ties between equal times.</param>
internal sealed record AlertCursor(DateTimeOffset OpenedAt, Guid Id)
{
    /// <summary>Turns the cursor into an opaque, URL-safe token for API clients.</summary>
    /// <returns>The token.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{OpenedAt.UtcTicks}:{Id:N}")));

    /// <summary>Reads a token made by <see cref="Encode"/>.</summary>
    /// <param name="token">The token from the API client.</param>
    /// <param name="cursor">The cursor, when the token is valid.</param>
    /// <returns><see langword="true"/> when the token is valid.</returns>
    public static bool TryDecode(string token, out AlertCursor? cursor)
    {
        cursor = null;

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)).Split(':');

            if (parts.Length == 2
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is >= 0 and <= 3155378975999999999
                && Guid.TryParseExact(parts[1], "N", out var id))
            {
                cursor = new AlertCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id);

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
