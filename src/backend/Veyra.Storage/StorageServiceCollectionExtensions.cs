using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core;

namespace Veyra.Storage;

public static class StorageServiceCollectionExtensions
{
    /// <summary>Registers the Storage module: PostgreSQL persistence, migrations, and retention.</summary>
    public static IServiceCollection AddVeyraStorage(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(VeyraOptionNames.Storage));

        return services;
    }
}
