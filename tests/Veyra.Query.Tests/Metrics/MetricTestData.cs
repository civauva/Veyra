using Veyra.Core.Metrics;

namespace Veyra.Query.Tests.Metrics;

/// <summary>Series as the Collector and the .NET SDK send them, for the view tests.</summary>
internal static class MetricTestData
{
    /// <summary>The histogram bounds of the tests' request durations: 0.1 s and 1 s.</summary>
    public static readonly double[] Bounds = [0.1, 1];

    /// <summary>A host metric of the Collector: no service, a host, and attributes.</summary>
    public static MetricSeries HostSeries(
        string name,
        MetricKind kind,
        bool monotonic,
        DateTimeOffset seen,
        params (string Key, string Value)[] attributes) => new(
            0,
            name,
            kind,
            "By",
            string.Empty,
            kind == MetricKind.Gauge ? MetricTemporality.Unspecified : MetricTemporality.Cumulative,
            monotonic,
            null,
            null,
            "web-01",
            new Dictionary<string, string> { ["host.name"] = "web-01", ["os.description"] = "Ubuntu 24.04" },
            attributes.ToDictionary(pair => pair.Key, pair => pair.Value),
            seen.AddDays(-1),
            seen);

    /// <summary>A request duration histogram of a .NET service in production for one status code.</summary>
    public static MetricSeries Durations(string service, int status, DateTimeOffset seen, string route = "/orders") => new(
        0,
        "http.server.request.duration",
        MetricKind.Histogram,
        "s",
        string.Empty,
        MetricTemporality.Cumulative,
        false,
        service,
        "production",
        "web-01",
        new Dictionary<string, string>
        {
            ["service.name"] = service,
            ["service.instance.id"] = service + "-1",
            ["telemetry.sdk.language"] = "dotnet",
        },
        new Dictionary<string, string>
        {
            ["http.response.status_code"] = status.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["http.route"] = route,
            ["http.request.method"] = "GET",
        },
        seen.AddDays(-1),
        seen);

    /// <summary>A cumulative histogram that grows by the given counts every minute from a start minute.</summary>
    public static void Grow(
        FakeMetricSearch store,
        MetricSeries series,
        DateTimeOffset firstMinute,
        int minutes,
        long[] perMinute)
    {
        for (var minute = 0; minute < minutes; minute++)
        {
            var counts = perMinute.Select(count => count * minute).ToArray();
            store.Point(series, firstMinute.AddMinutes(minute), null, new HistogramValue(counts.Sum(), null, null, null, Bounds, counts));
        }
    }
}
