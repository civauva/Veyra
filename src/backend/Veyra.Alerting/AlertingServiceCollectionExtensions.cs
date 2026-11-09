using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core;

namespace Veyra.Alerting;

/// <summary>
/// Registers the Alerting module with the dependency injection container. <c>Veyra.Api</c> calls it at startup.
/// </summary>
public static class AlertingServiceCollectionExtensions
{
    /// <summary>Registers the Alerting module: rule evaluation, incidents, and notifications.</summary>
    /// <param name="services">The service collection to add the module to.</param>
    /// <param name="configuration">
    /// The application configuration. The module reads its <c>Veyra:Alerting</c> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddVeyraAlerting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AlertingOptions>()
            .Bind(configuration.GetSection(VeyraOptionNames.Alerting))
            .Validate(o => o.EvaluationInterval > TimeSpan.Zero, "Veyra:Alerting:EvaluationInterval must be positive.")
            .ValidateOnStart();

        return services;
    }
}
