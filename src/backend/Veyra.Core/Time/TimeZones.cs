using System.Diagnostics.CodeAnalysis;

namespace Veyra.Core.Time;

/// <summary>
/// Time zones by their IANA names, such as <c>Europe/Berlin</c>, the names browsers report and people pick (VEY-22).
/// Windows names are refused, so the same name means the same zone in the browser, the API, and the database.
/// </summary>
public static class TimeZones
{
    /// <summary>Finds a zone by its IANA name.</summary>
    /// <param name="name">The IANA name.</param>
    /// <param name="zone">The zone, when found.</param>
    /// <returns>Whether the name is a known IANA zone.</returns>
    public static bool TryFind(string? name, [NotNullWhen(true)] out TimeZoneInfo? zone)
    {
        zone = null;

        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
        {
            return false;
        }

        // On Windows the runtime also accepts Windows names; only names that are IANA names count.
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(name, out var found)
            || !(found.HasIanaId || TimeZoneInfo.TryConvertIanaIdToWindowsId(name, out _)))
        {
            return false;
        }

        zone = found;

        return true;
    }

    /// <summary>
    /// The instant of midnight in a zone, a number of days before the local day of <paramref name="now"/>. Days in
    /// the zone can be 23 or 25 hours long around daylight saving changes, so days are counted on the local calendar.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="zone">The zone whose midnight counts.</param>
    /// <param name="daysBack">How many local days before today; 0 is the start of today.</param>
    /// <returns>That midnight as an instant in UTC.</returns>
    public static DateTimeOffset LocalMidnight(DateTimeOffset now, TimeZoneInfo zone, int daysBack)
    {
        ArgumentNullException.ThrowIfNull(zone);

        var local = TimeZoneInfo.ConvertTime(now, zone);
        var midnight = local.Date.AddDays(-daysBack);

        // A zone that skips midnight on a daylight saving day starts that day at the first hour that exists.
        while (zone.IsInvalidTime(midnight))
        {
            midnight = midnight.AddMinutes(30);
        }

        // As UTC, the only offset PostgreSQL accepts for timestamptz parameters.
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight)).ToUniversalTime();
    }

    /// <summary>The local calendar day of an instant in a zone.</summary>
    /// <param name="instant">The instant.</param>
    /// <param name="zone">The zone.</param>
    /// <returns>The day.</returns>
    public static DateOnly LocalDay(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
    }
}
