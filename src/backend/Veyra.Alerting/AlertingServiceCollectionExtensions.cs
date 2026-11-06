using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core;

namespace Veyra.Alerting;

public static class AlertingServiceCollectionExtensions
{
    /// <summary>Registers the Alerting module: rule evaluation, incidents, and notifications.</summary>
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
