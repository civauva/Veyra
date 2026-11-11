using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Veyra.Core.Metrics;

namespace Veyra.Storage.Metrics;

/// <summary>
/// Computes the identity of a series: a SHA-256 hash over its name, kind, unit, temporality, monotonicity, and every
/// resource and point attribute, sorted by key (ADR 0010). The description is not part of it, so a sender that
/// rewords a description keeps its series.
/// </summary>
internal static class MetricSeriesIdentity
{
    /// <summary>The hash of a point's series.</summary>
    /// <param name="point">The point.</param>
    /// <returns>32 bytes.</returns>
    public static byte[] Of(MetricPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);

        var text = new StringBuilder()
            .Append(point.Name).Append('\0')
            .Append(((int)point.Kind).ToString(CultureInfo.InvariantCulture)).Append('\0')
            .Append(point.Unit).Append('\0')
            .Append(((int)point.Temporality).ToString(CultureInfo.InvariantCulture)).Append('\0')
            .Append(point.Monotonic ? '1' : '0').Append('\0');

        Append(text, point.ResourceAttributes);
        text.Append('\u0001');
        Append(text, point.Attributes);

        return SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
    }

    // Each key and value ends with a separator that cannot appear in OTLP text, so "a=bc" and "ab=c" differ.
    private static void Append(StringBuilder text, IReadOnlyDictionary<string, string> attributes)
    {
        foreach (var (key, value) in attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            text.Append(key).Append('\u0002').Append(value).Append('\u0003');
        }
    }
}
