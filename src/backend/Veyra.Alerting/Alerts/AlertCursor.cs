using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Veyra.Core.Alerts;

namespace Veyra.Alerting.Alerts;

/// <summary>
/// Marks where a page of alerts ended, so the next page continues right after it (keyset paging, as for logs).
/// </summary>
/// <param name="OpenedAt">When the last alert on the page opened.</param>
/// <param name="Id">The ID of the last alert on the page, which breaks ties between equal times.</param>
/// <param name="Severity">
/// The severity of the last alert on a page listed worst first (VEY-68); <see langword="null"/> for the newest-first
/// order.
/// </param>
internal sealed record AlertCursor(DateTimeOffset OpenedAt, Guid Id, AlertSeverity? Severity = null)
{
    /// <summary>Turns the cursor into an opaque, URL-safe token for API clients.</summary>
    /// <returns>The token.</returns>
    public string Encode() =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            Severity is { } severity
                ? string.Create(CultureInfo.InvariantCulture, $"{OpenedAt.UtcTicks}:{Id:N}:{(int)severity}")
                : string.Create(CultureInfo.InvariantCulture, $"{OpenedAt.UtcTicks}:{Id:N}")));

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

            if (parts.Length is 2 or 3
                && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks is >= 0 and <= 3155378975999999999
                && Guid.TryParseExact(parts[1], "N", out var id))
            {
                AlertSeverity? severity = null;

                if (parts.Length == 3)
                {
                    if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var level)
                        || !Enum.IsDefined((AlertSeverity)level))
                    {
                        return false;
                    }

                    severity = (AlertSeverity)level;
                }

                cursor = new AlertCursor(new DateTimeOffset(ticks, TimeSpan.Zero), id, severity);

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
