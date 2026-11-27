using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Veyra.Core;
using Veyra.Core.Agents;
using Veyra.Core.Settings;
using Veyra.Core.Telemetry;
using Veyra.Ingestion.Otlp;
using Veyra.Ingestion.Pipeline;
using Veyra.Ingestion.Settings;

namespace Veyra.Ingestion;

/// <summary>
/// Registers the Ingestion module with the dependency injection container. <c>Veyra.Api</c> calls it at startup.
/// </summary>
public static class IngestionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Ingestion module: OTLP receivers for logs and metrics, normalization, and buffering.
    /// </summary>
    /// <param name="services">The service collection to add the module to.</param>
    /// <param name="configuration">
    /// The application configuration. The module reads its <c>Veyra:Ingestion</c> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddVeyraIngestion(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(VeyraOptionNames.Ingestion))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IngestionOptions>, IngestionOptionsRules>();
        services.AddSingleton<IModuleSettings, IngestionSettings>();

        services.TryAddSingleton(TimeProvider.System);

        // The writers tell the live streams what they stored. The Query module registers the real hub, which wins
        // over this stand-in whichever module is added first.
        services.TryAddSingleton<ILiveTelemetry, NoLiveTelemetry>();

        // Probe answers go to the Agents module, whose registration wins over this stand-in in the same way.
        services.TryAddSingleton<IAgentProbeReports, NoAgentProbeReports>();
        services.AddSingleton<FailedKeyThrottle>();
        services.AddSingleton<LogQueue>();
        services.AddSingleton<ActivityQueue>();
        services.AddHostedService<ActivityWriterService>();
        services.AddSingleton<LogPipeline>();
        services.AddHostedService<LogWriterService>();
        services.AddSingleton<MetricQueue>();
        services.AddSingleton<MetricPipeline>();
        services.AddHostedService<MetricWriterService>();
        services.AddSingleton<SpanQueue>();
        services.AddSingleton<SpanPipeline>();
        services.AddHostedService<SpanWriterService>();

        // Only the OTLP services are offered. Unknown gRPC services get the standard UNIMPLEMENTED answer.
        services.AddGrpc(options => options.IgnoreUnknownServices = true);

        return services;
    }

    /// <summary>
    /// Maps the OTLP endpoints. They are open to anonymous callers because they check ingestion keys themselves.
    /// </summary>
    /// <param name="endpoints">The application's endpoint builder.</param>
    /// <returns>The same <paramref name="endpoints"/> instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is null.</exception>
    public static IEndpointRouteBuilder MapVeyraOtlp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        OtlpHttpEndpoint.Map(endpoints);
        endpoints.MapGrpcService<OtlpGrpcLogsService>().AllowAnonymous();
        endpoints.MapGrpcService<OtlpGrpcMetricsService>().AllowAnonymous();
        endpoints.MapGrpcService<OtlpGrpcTraceService>().AllowAnonymous();

        return endpoints;
    }
}
