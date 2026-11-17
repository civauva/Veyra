using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Veyra.Core;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Settings;
using Veyra.Query.Blocking;
using Veyra.Query.Home;
using Veyra.Query.Live;
using Veyra.Query.Metrics;
using Veyra.Query.Queries;
using Veyra.Query.Server;
using Veyra.Query.Settings;
using Veyra.Query.Storage;

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
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<QueryOptions>, QueryOptionsRules>();
        services.AddSingleton<IModuleSettings, QuerySettings>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<HomeSnapshotBuilder>();
        services.AddSingleton(provider => new SharedHomeSnapshots(
            provider.GetRequiredService<HomeSnapshotBuilder>().BuildAsync,
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<MetricViewBuilder>();
        services.AddSingleton<DatabaseViewBuilder>();
        services.AddSingleton<LiveViewBuilder>();
        services.AddSingleton<QueriesViewBuilder>();
        services.AddSingleton<BlockingViewBuilder>();
        services.AddSingleton<StorageViewBuilder>();
        services.AddSingleton<IndexViewBuilder>();
        services.AddSingleton<ServerViewBuilder>();
        services.AddSingleton<JobsViewBuilder>();
        services.AddSingleton<EventsViewBuilder>();
        services.AddSingleton<AvailabilityViewBuilder>();
        services.AddSingleton<ConfigurationViewBuilder>();
        services.AddSingleton<PermissionsViewBuilder>();
        services.AddSingleton<IMetricHealth, MetricHealth>();

        // The controller is public, as MVC requires, but its builder is internal, so the module builds it. The host's
        // AddControllersAsServices() only adds controllers that are not registered yet.
        services.AddTransient(provider => new HomeController(provider.GetRequiredService<SharedHomeSnapshots>()));
        services.AddTransient(provider => new MetricsController(
            provider.GetRequiredService<MetricViewBuilder>(),
            provider.GetRequiredService<DatabaseViewBuilder>()));
        services.AddTransient(provider => new DatabaseLiveController(provider.GetRequiredService<LiveViewBuilder>()));
        services.AddTransient(provider =>
            new DatabaseQueriesController(provider.GetRequiredService<QueriesViewBuilder>()));
        services.AddTransient(provider =>
            new DatabaseBlockingController(provider.GetRequiredService<BlockingViewBuilder>()));
        services.AddTransient(provider =>
            new DatabaseStorageController(
                provider.GetRequiredService<StorageViewBuilder>(),
                provider.GetRequiredService<IndexViewBuilder>()));
        services.AddTransient(provider =>
            new DatabaseServerController(
                provider.GetRequiredService<ServerViewBuilder>(),
                provider.GetRequiredService<JobsViewBuilder>(),
                provider.GetRequiredService<EventsViewBuilder>(),
                provider.GetRequiredService<AvailabilityViewBuilder>(),
                provider.GetRequiredService<ConfigurationViewBuilder>(),
                provider.GetRequiredService<PermissionsViewBuilder>()));

        return services;
    }
}
