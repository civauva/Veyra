using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Veyra.Storage;

/// <summary>
/// Reports whether Veyra can reach its PostgreSQL database. It backs the <c>/readyz</c> endpoint.
/// </summary>
/// <remarks>
/// A load balancer or reverse proxy uses readiness to decide whether to send traffic to this instance. Liveness,
/// <c>/healthz</c>, stays independent of the database, so a short database outage does not get the container restarted.
/// </remarks>
/// <param name="dataSource">
/// The database connection pool, or <see langword="null"/> when no connection string is configured.
/// </param>
internal sealed class StorageHealthCheck(NpgsqlDataSource? dataSource = null) : IHealthCheck
{
    /// <summary>What to do when no connection string is configured.</summary>
    internal const string NotConfiguredMessage = "Storage is not configured. Set Veyra__Storage__ConnectionString.";

    /// <summary>
    /// Opens a connection and runs a trivial query.
    /// </summary>
    /// <param name="context">The health check context supplied by ASP.NET Core.</param>
    /// <param name="cancellationToken">Cancels the check when the request is aborted.</param>
    /// <returns>Healthy when the query succeeds; unhealthy when storage is not configured or unreachable.</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (dataSource is null)
        {
            return HealthCheckResult.Unhealthy(NotConfiguredMessage);
        }

        try
        {
            await using var command = dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (NpgsqlException exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL is not reachable.", exception);
        }
    }
}
