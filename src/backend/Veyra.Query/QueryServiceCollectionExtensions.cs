using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Veyra.Core;
using Veyra.Core.Metrics.Health;
using Veyra.Query.Home;
using Veyra.Query.Metrics;

namespace Veyra.Query;

/// <summary>
/// Registers the Query module with the dependency injection container. <c>Veyra.Api</c> calls it at startup.
/// </summary>
public static class QueryServiceCollectionExtensions
{
    /// <summary>Registers the Query module: search, filtering, and cross-signal correlation.</summary>
    /// <param name="services">The service collection to add the module to.</param>
    /// <param name="configuration">
    /// The application configuration. The module reads its <c>Veyra:Query</c> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddVeyraQuery(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<QueryOptions>()
            .Bind(configuration.GetSection(VeyraOptionNames.Query))
            .Validate(o => o.MaxPageSize > 0, "Veyra:Query:MaxPageSize must be greater than zero.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HomeSnapshotBuilder>();
        services.AddSingleton<MetricViewBuilder>();
        services.AddSingleton<IMetricHealth, MetricHealth>();

        // The controller is public, as MVC requires, but its builder is internal, so the module builds it. The host's
        // AddControllersAsServices() only adds controllers that are not registered yet.
        services.AddTransient(provider => new HomeController(provider.GetRequiredService<HomeSnapshotBuilder>()));
        services.AddTransient(provider => new MetricsController(provider.GetRequiredService<MetricViewBuilder>()));

        return services;
    }
}
