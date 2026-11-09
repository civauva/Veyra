using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Veyra.Core;

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

        return services;
    }
}
