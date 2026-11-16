using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Veyra.Agents.Agents;
using Veyra.Agents.Api;
using Veyra.Agents.Enrollment;
using Veyra.Agents.Install;
using Veyra.Agents.OpAmp;
using Veyra.Agents.SqlServers;
using Veyra.Core;
using Veyra.Core.Schema;

namespace Veyra.Agents;

/// <summary>
/// Registers the Agents module with the dependency injection container, maps its OpAMP endpoint, and sets up the
/// built-in agent. <c>Veyra.Api</c> calls all three at startup.
/// </summary>
public static class AgentsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Agents module: Veyra's agents, the SQL Servers they read, and the OpAMP server that configures
    /// them (ADR 0019).
    /// </summary>
    /// <remarks>
    /// The module issues agent keys through <see cref="Veyra.Core.Security.IIngestionKeyIssuer"/> and checks them
    /// through <see cref="Veyra.Core.Security.IIngestionKeyValidator"/>, which the Access module registers. It reads
    /// the agents' own logs and the servers' figures through <see cref="Veyra.Core.Telemetry.ILogSearch"/> and
    /// <see cref="Veyra.Core.Metrics.IMetricSearch"/>, which the Storage module registers.
    /// </remarks>
    /// <param name="services">The service collection to add the module to.</param>
    /// <param name="configuration">
    /// The application configuration. The module reads its <c>Veyra:Agents</c> section.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> or <paramref name="configuration"/> is null.
    /// </exception>
    public static IServiceCollection AddVeyraAgents(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AgentsOptions>()
            .Bind(configuration.GetSection(VeyraOptionNames.Agents))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AgentsOptions>, AgentsOptionsRules>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISchemaMigrationSource, AgentsMigrations>();

        // Passwords are encrypted with the key ring that already protects cookies and webhook URLs.
        services.AddDataProtection();

        services.AddSingleton<AgentStore>();
        services.AddSingleton<BuiltInAgentSetup>();
        services.AddSingleton<SqlServerStore>();
        services.AddSingleton<SqlServerPasswordProtector>();
        services.AddSingleton<SqlServerService>();
        services.AddSingleton<SqlServerSignals>();
        services.AddSingleton<AgentsOverview>();
        services.AddSingleton<OpAmpServer>();
        services.AddSingleton<EnrollmentStore>();
        services.AddSingleton<EnrollThrottle>();
        services.AddSingleton<AgentEnrollment>();

        services.AddTransient(provider => new AgentsController(
            provider.GetRequiredService<AgentsOverview>(),
            provider.GetRequiredService<AgentEnrollment>()));
        services.AddTransient(provider => new SqlServersController(
            provider.GetRequiredService<SqlServerService>(),
            provider.GetRequiredService<AgentsOverview>()));

        return services;
    }

    /// <summary>
    /// Maps <c>POST /v1/opamp</c>, where agents poll for their configuration. It is open to anonymous callers because
    /// it checks the agent's key itself.
    /// </summary>
    /// <param name="endpoints">The application's endpoint builder.</param>
    /// <returns>The same <paramref name="endpoints"/> instance, so calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpoints"/> is null.</exception>
    public static IEndpointRouteBuilder MapVeyraAgents(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        OpAmpEndpoint.Map(endpoints);
        AgentInstallEndpoints.Map(endpoints);

        return endpoints;
    }

    /// <summary>
    /// Sets up the built-in agent when Veyra runs in its Docker Compose deployment: creates it on first start and
    /// writes its start-up file with its key into the folder it shares with the agent's container.
    /// </summary>
    /// <param name="services">The application's services.</param>
    /// <param name="cancellationToken">Cancels startup.</param>
    /// <returns>A task that completes when the agent can connect.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static Task PrepareVeyraAgentsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.GetRequiredService<BuiltInAgentSetup>().PrepareAsync(cancellationToken);
    }
}
