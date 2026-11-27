using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Veyra.Agents.Probes;

/// <summary>
/// Finishes probes in the background (ADR 0028): with each answer as it arrives, and every few seconds those whose
/// connection failed or whose time ran out, so a probe's password is forgotten even when nobody reads the probe.
/// </summary>
/// <param name="reports">The answers ingestion queued.</param>
/// <param name="services">
/// Gives the probe service when it is first needed, as other background services do, so Veyra starts without a
/// database configured and says so on its readiness check rather than failing to start.
/// </param>
/// <param name="clock">The clock for the sweeps.</param>
/// <param name="logger">Records a sweep or an answer that failed, so the worker keeps running.</param>
internal sealed partial class ProbeWorker(
    ProbeReports reports,
    IServiceProvider services,
    TimeProvider clock,
    ILogger<ProbeWorker> logger) : BackgroundService
{
    /// <summary>How often pending probes are checked for errors and time-outs.</summary>
    public static readonly TimeSpan SweepEvery = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var answers = Task.Run(() => AnswerAsync(stoppingToken), stoppingToken);

        using var timer = new PeriodicTimer(SweepEvery, clock);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await services.GetRequiredService<ProbeService>().SweepAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogSweepFailed(exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Veyra is stopping.
        }

        await answers;
    }

    private async Task AnswerAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var report in reports.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await services.GetRequiredService<ProbeService>().CompleteAsync(report, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogAnswerFailed(exception);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Veyra is stopping.
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Checking the pending probes failed")]
    private partial void LogSweepFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A probe's answer could not be stored")]
    private partial void LogAnswerFailed(Exception exception);
}
