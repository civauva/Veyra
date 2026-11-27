using Veyra.Core.Alerts;

namespace Veyra.Agents.Tests;

/// <summary>Counts as many notification channels as a test says.</summary>
internal sealed class FakeNotificationChannels : INotificationChannels
{
    /// <summary>The number of channels switched on.</summary>
    public int Enabled { get; set; }

    /// <inheritdoc />
    public Task<int> CountEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(Enabled);
}
