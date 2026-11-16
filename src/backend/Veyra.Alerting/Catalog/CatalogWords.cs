using System.Globalization;

namespace Veyra.Alerting.Catalog;

/// <summary>
/// The words of catalog rules' alerts and notifications (ADR 0020): a measure in its unit, and a rule's headline and
/// recovery with the server, part, and value filled in.
/// </summary>
internal static class CatalogWords
{
    /// <summary>A measurement in its rule's unit: "3 min", "42%", "3 requests", "1.2 GB", "9 days", or "yes".</summary>
    /// <param name="rule">The rule.</param>
    /// <param name="value">The measurement.</param>
    /// <returns>The text.</returns>
    public static string Value(CatalogRule rule, double value)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return rule.Unit switch
        {
            MeasureUnit.Seconds => Seconds(value),
            MeasureUnit.Ratio => (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%",
            MeasureUnit.Count => Count(value, rule.Noun ?? "item"),
            MeasureUnit.Bytes => Bytes(value),
            MeasureUnit.Days => Count(value, "day"),
            _ => value >= 1 ? "yes" : "no",
        };
    }

    /// <summary>Fills in a rule's headline or recovery.</summary>
    /// <param name="template">The headline or recovery.</param>
    /// <param name="server">The server in words, such as "SQL Server sql-prod-01".</param>
    /// <param name="part">The part, or <see langword="null"/>.</param>
    /// <param name="value">The value in words.</param>
    /// <returns>The sentence.</returns>
    public static string Fill(string template, string server, string? part, string value)
    {
        ArgumentNullException.ThrowIfNull(template);

        return template
            .Replace("{server}", server, StringComparison.Ordinal)
            .Replace("{part}", part ?? "", StringComparison.Ordinal)
            .Replace("{value}", value, StringComparison.Ordinal);
    }

    // "45 s" below two minutes, then "36 min", "1 h 36 min", "2 d 3 h".
    private static string Seconds(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));

        if (span.TotalSeconds < 120)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:0} s");
        }

        if (span.TotalDays >= 1)
        {
            return span.Hours == 0 ? $"{(int)span.TotalDays} d" : $"{(int)span.TotalDays} d {span.Hours} h";
        }

        if (span.TotalHours >= 1)
        {
            return span.Minutes == 0 ? $"{(int)span.TotalHours} h" : $"{(int)span.TotalHours} h {span.Minutes} min";
        }

        return $"{(int)span.TotalMinutes} min";
    }

    private static string Count(double value, string noun)
    {
        var whole = (long)Math.Round(value, MidpointRounding.AwayFromZero);

        return whole == 1 ? $"1 {noun}" : string.Create(CultureInfo.InvariantCulture, $"{whole:N0} {noun}s");
    }

    private static string Bytes(double bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1L << 30):0.#} GB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1L << 20):0} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024:0} KB"),
    };
}
