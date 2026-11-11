using Veyra.Core.Telemetry;

namespace Veyra.Alerting.Tests.ErrorRates;

/// <summary>Returns whatever rates a test sets, and remembers the window it was asked for.</summary>
internal sealed class FakeLogRates : ILogRates
{
    public IReadOnlyList<ServiceLogRate> Rates { get; set; } = [];

    public (DateTimeOffset Start, DateTimeOffset End)? LastWindow { get; private set; }

    public Task<IReadOnlyList<ServiceLogRate>> GetServiceRatesAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        CancellationToken cancellationToken)
    {
        LastWindow = (windowStart, windowEnd);

        return Task.FromResult(Rates);
    }
}
