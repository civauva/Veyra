using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Veyra.Core;
using Veyra.Core.Alerts;
using Veyra.Core.Metrics.Health;
using Veyra.Core.Settings;
using Veyra.Core.Telemetry;
using Veyra.Query.Backups;
using Veyra.Query.Blocking;
using Veyra.Query.Home;
using Veyra.Query.Incidents;
using Veyra.Query.Jobs;
using Veyra.Query.Live;
using Veyra.Query.Metrics;
using Veyra.Query.Overview;
using Veyra.Query.Queries;
using Veyra.Query.Resources;
using Veyra.Query.Search;
using Veyra.Query.Server;
using Veyra.Query.ServerOverview;
using Veyra.Query.Settings;
using Veyra.Query.Storage;
using Veyra.Query.Streams;

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
        services.AddSingleton<BackupsViewBuilder>();
        services.AddSingleton<ServerOverviewBuilder>();
        services.AddSingleton<ServiceListBuilder>();
        services.AddSingleton<HostListBuilder>();
        services.AddSingleton<DatabaseListBuilder>();
        services.AddSingleton<OverviewBuilder>();
        services.AddSingleton<JobsBuilder>();
        services.AddSingleton<IMetricHealth, MetricHealth>();

        // Incidents (ADR 0026): the Alerting module groups alerts with the relations read here, and the incident's
        // context is gathered here from the incident Alerting keeps; an alert's context too (ADR 0029).
        services.AddSingleton<BlockRelations>();
        services.AddSingleton<IBlockRelations>(provider => provider.GetRequiredService<BlockRelations>());
        services.AddSingleton<IncidentContextBuilder>();
        services.AddSingleton<AlertContextBuilder>();

        // The live streams (ADR 0022). Ingestion's writers hand the hub what they store; it replaces Ingestion's
        // stand-in, so it is added, not tried.
        services.AddSingleton<LiveTelemetryHub>();
        services.AddSingleton<ILiveTelemetry>(provider => provider.GetRequiredService<LiveTelemetryHub>());
        services.AddHostedService(provider => provider.GetRequiredService<LiveTelemetryHub>());

        // Search across signals (ADR 0022). Tests replace the budget to make a group run out.
        services.AddSingleton(SearchBudget.Default);
        services.AddSingleton<SearchBuilder>();

        // The controller is public, as MVC requires, but its builder is internal, so the module builds it. The host's
        // AddControllersAsServices() only adds controllers that are not registered yet.
        services.AddTransient(provider => new HomeController(provider.GetRequiredService<SharedHomeSnapshots>()));
        services.AddTransient(provider => new MetricsController(
            provider.GetRequiredService<MetricViewBuilder>(),
            provider.GetRequiredService<DatabaseViewBuilder>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddTransient(provider => new DatabaseLiveController(provider.GetRequiredService<LiveViewBuilder>()));
        services.AddTransient(provider =>
            new DatabaseQueriesController(
                provider.GetRequiredService<QueriesViewBuilder>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddTransient(provider =>
            new DatabaseBlockingController(
                provider.GetRequiredService<BlockingViewBuilder>(),
                provider.GetRequiredService<TimeProvider>()));
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
                provider.GetRequiredService<PermissionsViewBuilder>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddTransient(provider =>
            new DatabaseOverviewController(
                provider.GetRequiredService<ServerOverviewBuilder>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddTransient(provider =>
            new DatabaseBackupsController(
                provider.GetRequiredService<BackupsViewBuilder>(),
                provider.GetRequiredService<TimeProvider>()));

        services.AddTransient(provider =>
            new ResourcesController(
                provider.GetRequiredService<ServiceListBuilder>(),
                provider.GetRequiredService<HostListBuilder>(),
                provider.GetRequiredService<DatabaseListBuilder>(),
                provider.GetRequiredService<IOptionsMonitor<QueryOptions>>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddTransient(provider =>
            new OverviewController(
                provider.GetRequiredService<OverviewBuilder>(),
                provider.GetRequiredService<IAuthorizationService>(),
                provider.GetRequiredService<TimeProvider>()));

        services.AddTransient(provider =>
            new IncidentContextController(provider.GetRequiredService<IncidentContextBuilder>()));
        services.AddTransient(provider =>
            new AlertContextController(provider.GetRequiredService<AlertContextBuilder>()));
        services.AddTransient(provider =>
            new JobsController(
                provider.GetRequiredService<JobsBuilder>(),
                provider.GetRequiredService<TimeProvider>()));

        services.AddTransient(provider => new SearchController(
            provider.GetRequiredService<SearchBuilder>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddTransient(provider => new LiveController(
            provider.GetRequiredService<LiveTelemetryHub>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<JsonOptions>>()));
        services.AddTransient(provider => new LogStreamController(
            provider.GetRequiredService<LiveTelemetryHub>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<IOptions<JsonOptions>>()));

        return services;
    }
}
