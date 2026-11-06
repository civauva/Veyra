using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core;

namespace Veyra.Query;

public static class QueryServiceCollectionExtensions
{
    /// <summary>Registers the Query module: search, filtering, and cross-signal correlation.</summary>
    public static IServiceCollection AddVeyraQuery(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<QueryOptions>()
            .Bind(configuration.GetSection(VeyraOptionNames.Query))
            .Validate(o => o.MaxPageSize > 0, "Veyra:Query:MaxPageSize must be greater than zero.")
            .ValidateOnStart();

        return services;
    }
}
