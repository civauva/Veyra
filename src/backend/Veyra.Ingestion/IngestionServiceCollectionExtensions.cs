using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core;

namespace Veyra.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    /// <summary>Registers the Ingestion module: OTLP receivers, normalization, and buffering.</summary>
    public static IServiceCollection AddVeyraIngestion(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(VeyraOptionNames.Ingestion))
            .Validate(o => o.MaxQueueLength > 0, "Veyra:Ingestion:MaxQueueLength must be greater than zero.")
            .ValidateOnStart();

        return services;
    }
}
