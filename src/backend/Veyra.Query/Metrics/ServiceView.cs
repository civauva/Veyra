using System.Globalization;
using Veyra.Core.Metrics;
using Veyra.Query.Metrics.Api;

namespace Veyra.Query.Metrics;

/// <summary>
/// Reads one service's metrics from the OpenTelemetry SDK: HTTP server request durations, and process and runtime
/// metrics where the SDK sends them.
/// </summary>
internal static class ServiceView
{
    private const int _maxRoutes = 10;

    /// <summary>The numbers of a service's overview card.</summary>
    /// <param name="service">The service name.</param>
    /// <param name="environment">The environment, if any.</param>
    /// <param name="window">The service's series over the chosen window.</param>
    /// <param name="recent">The same series over the last five minutes.</param>
    /// <returns>The card.</returns>
    public static ServiceCardResponse Card(string service, string? environment, SeriesData window, SeriesData recent)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(recent);

        var any = window.Series.Concat(recent.Series).ToList();
        var durations = recent.Total(recent.Increases(recent.Named(MetricNames.RequestDuration)));
        var errors = recent.Total(recent.Increases(ServerErrors(recent)));

        return new ServiceCardResponse(
            Name: service,
            Environment: environment,
            Runtime: Runtime(any),
            Instances: Instances(any).Count,
            LastSeen: recent.LastSent(any, window.Window.End),
            RequestsPerSecond: durations is null ? null : durations.Count / recent.Seconds,
            P95Seconds: durations?.Quantile(0.95),
            ServerErrorRatio: durations is { Count: > 0 } ? (errors?.Count ?? 0) / (double)durations.Count : null,
            CpuRatio: recent.Average(Cpu(recent)),
            MemoryBytes: recent.Latest(Memory(recent)),
            Latency: window.Window.Line("p95", Quantiles(window, 0.95)));
    }

    /// <summary>A service's metrics over a window.</summary>
    /// <param name="service">The service name.</param>
    /// <param name="environment">The environment, if any.</param>
    /// <param name="window">The service's series over the chosen window.</param>
    /// <param name="recent">The same series over the last five minutes.</param>
    /// <returns>The detail.</returns>
    public static ServiceMetricsResponse Detail(
        string service,
        string? environment,
        SeriesData window,
        SeriesData recent)
    {
        ArgumentNullException.ThrowIfNull(window);

        var lines = window.Window;
        var seconds = lines.Bucket.TotalSeconds;
        var durations = window.Named(MetricNames.RequestDuration).ToList();

        Dictionary<DateTimeOffset, double> RequestsWhere(Func<int, bool> status) =>
            window.Increases(durations.Where(series => StatusCode(series) is { } code && status(code)))
                .ToDictionary(pair => pair.Key, pair => pair.Value.Count / seconds);

        return new ServiceMetricsResponse(
            Card(service, environment, window, recent),
            lines.Range,
            (int)seconds,
            Instances(window.Series.Concat(recent.Series)),
            [
                lines.Line("p50", Quantiles(window, 0.5)),
                lines.Line("p95", Quantiles(window, 0.95)),
                lines.Line("p99", Quantiles(window, 0.99)),
            ],
            [
                lines.Line("ok", RequestsWhere(code => code < 400)),
                lines.Line("4xx", RequestsWhere(code => code is >= 400 and < 500)),
                lines.Line("5xx", RequestsWhere(code => code >= 500)),
            ],
            Routes(recent),
            [
                lines.Line("cpu", Cpu(window)),
                lines.Line("memory", Memory(window)),
            ],
            [
                lines.Line(
                    "gc",
                    window.Rates(window.Named(MetricNames.DotnetGcCollections))
                        .ToDictionary(pair => pair.Key, pair => pair.Value * 60)),
                lines.Line("queue", window.Levels(window.Named(MetricNames.DotnetThreadPoolQueue))),
                lines.Line("threads", window.Levels(window.Named(MetricNames.DotnetThreadPoolThreads))),
            ]);
    }

    /// <summary>A quantile of the request durations, per bucket.</summary>
    /// <param name="data">The service's series.</param>
    /// <param name="quantile">From 0 to 1.</param>
    /// <returns>The duration in seconds per bucket start.</returns>
    public static Dictionary<DateTimeOffset, double> Quantiles(SeriesData data, double quantile)
    {
        ArgumentNullException.ThrowIfNull(data);

        return data.Increases(data.Named(MetricNames.RequestDuration))
            .Select(pair => (pair.Key, Value: pair.Value.Quantile(quantile)))
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!.Value);
    }

    // CPU seconds per second over the CPUs the processes may use. Without a CPU count, cores in use.
    private static Dictionary<DateTimeOffset, double> Cpu(SeriesData data)
    {
        var dotnet = data.Named(MetricNames.DotnetCpuTime).ToList();
        var used = data.Rates(dotnet.Count > 0 ? dotnet : data.Named(MetricNames.ProcessCpuTime));
        var cpus = data.Levels(data.Named(MetricNames.DotnetCpuCount));

        return used.ToDictionary(
            pair => pair.Key,
            pair => cpus.GetValueOrDefault(pair.Key) is > 0 and var count ? pair.Value / count : pair.Value);
    }

    private static Dictionary<DateTimeOffset, double> Memory(SeriesData data)
    {
        var dotnet = data.Named(MetricNames.DotnetWorkingSet).ToList();

        return data.Levels(dotnet.Count > 0 ? dotnet : data.Named(MetricNames.ProcessMemory));
    }

    private static IEnumerable<MetricSeries> ServerErrors(SeriesData data) =>
        data.Named(MetricNames.RequestDuration).Where(series => StatusCode(series) >= 500);

    /// <summary>The busiest routes of a service over a window, with their request rates and latencies.</summary>
    /// <param name="recent">The service's request durations over the window.</param>
    /// <returns>Up to ten routes, busiest first.</returns>
    public static List<RouteResponse> Routes(SeriesData recent)
    {
        ArgumentNullException.ThrowIfNull(recent);

        var seconds = recent.Seconds;

        return recent.Named(MetricNames.RequestDuration)
            .GroupBy(series => (
                Method: MetricNames.Attribute(series, "http.request.method", "http.method"),
                Route: MetricNames.Attribute(series, "http.route") ?? "(none)"))
            .Select(group =>
            {
                var total = recent.Total(recent.Increases(group));
                var errors = recent.Total(recent.Increases(group.Where(series => StatusCode(series) >= 500)));

                return total is null
                    ? null
                    : new RouteResponse(
                        group.Key.Method,
                        group.Key.Route,
                        total.Count / seconds,
                        total.Quantile(0.95),
                        total.Count == 0 ? null : (errors?.Count ?? 0) / (double)total.Count);
            })
            .OfType<RouteResponse>()
            .Where(route => route.RequestsPerSecond > 0)
            .OrderByDescending(route => route.RequestsPerSecond)
            .Take(_maxRoutes)
            .ToList();
    }

    private static int? StatusCode(MetricSeries series) =>
        int.TryParse(
            MetricNames.Attribute(series, "http.response.status_code", "http.status_code"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var code)
            ? code
            : null;

    // An instance is its service.instance.id, or its host without one. The SDKs make up a GUID when the ID is not
    // configured; its first eight characters tell instances apart and fit on a tag.
    private static List<string> Instances(IEnumerable<MetricSeries> series) =>
        series
            .Select(one => one.ResourceAttributes.GetValueOrDefault("service.instance.id") ?? one.HostName)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Select(instance => Guid.TryParse(instance, out _) ? instance[..8] : instance)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string? Runtime(IEnumerable<MetricSeries> series) =>
        series.Select(one => one.ResourceAttributes.GetValueOrDefault("telemetry.sdk.language"))
            .FirstOrDefault(language => language is not null) switch
        {
            null => null,
            "dotnet" => ".NET",
            "nodejs" => "Node.js",
            "python" => "Python",
            "java" => "Java",
            "go" => "Go",
            var other => other,
        };
}
