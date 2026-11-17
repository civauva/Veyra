using System.Security.Claims;
using Veyra.Core.Settings;

namespace Veyra.Agents.Tests;

/// <summary>Keeps the changes the Agents module reports for the settings history, for tests to look at.</summary>
internal sealed class FakeMonitoringHistory : IMonitoringHistory
{
    /// <summary>The signed-in Admin the tests make changes as.</summary>
    public static ClaimsPrincipal Admin { get; } = new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "0193f7a4-6f8e-7c1a-9d2b-3c4d5e6f7a8b")],
        "Test"));

    /// <summary>The changes reported, oldest first, with who made them.</summary>
    public List<(MonitoringChange Change, ClaimsPrincipal By)> Changes { get; } = [];

    /// <inheritdoc />
    public Task RecordAsync(MonitoringChange change, ClaimsPrincipal by, CancellationToken cancellationToken)
    {
        Changes.Add((change, by));

        return Task.CompletedTask;
    }
}
