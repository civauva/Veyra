using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Veyra.Alerting.Incidents;

/// <summary>
/// Marks where a page of incidents ended, so the next page continues right after it (keyset paging, as for alerts).
/// </summary>
/// <param name="Resolved">Whether the last incident on the page was resolved: the unresolved ones come first.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="Id">Its ID, which breaks ties between equal times.</param>
internal sealed record IncidentCursor(bool Resolved, DateTimeOffset OpenedAt, Guid Id)
{
    /// <summary>Turns the cursor into an opaque, URL-safe token for API clients.</summary>
    /// <returns>The token.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{(Resolved ? 1 : 0)}:{OpenedAt.UtcTicks}:{Id:N}")));

    /// <summary>Reads a token made by <see cref="Encode"/>.</summary>
    /// <param name="token">The token from the API client.</param>
    /// <param name="cursor">The cursor, when the token is valid.</param>
    /// <returns><see langword="true"/> when the token is valid.</returns>
    public static bool TryDecode(string token, out IncidentCursor? cursor)
    {
        cursor = null;

        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token)).Split(':');

            if (parts.Length == 3
                && parts[0] is "0" or "1"
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is >= 0 and <= 3155378975999999999
                && Guid.TryParseExact(parts[2], "N", out var id))
            {
                cursor = new IncidentCursor(parts[0] == "1", new DateTimeOffset(ticks, TimeSpan.Zero), id);

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
