using Veyra.Agents.Agents;

namespace Veyra.Agents.Api;

/// <summary>An agent as the Agents page shows it.</summary>
/// <param name="Id">The agent's ID.</param>
/// <param name="Name">Its name, such as "Built in".</param>
/// <param name="Placement">Where it runs.</param>
/// <param name="State">How it stands.</param>
/// <param name="Version">The Collector's version, once the agent has said.</param>
/// <param name="HostName">The machine or container it runs on, once the agent has said.</param>
/// <param name="OperatingSystem">Its operating system, once the agent has said.</param>
/// <param name="LastSeenAt">When it last polled; <see langword="null"/> for never.</param>
/// <param name="Error">Why its configuration failed or its Collector is unhealthy, in the Collector's words.</param>
/// <param name="Servers">How many database servers it reads, SQL Server and PostgreSQL.</param>
/// <param name="CreatedAt">When it was set up.</param>
public sealed record AgentResponse(
    Guid Id,
    string Name,
    AgentPlacement Placement,
    AgentState State,
    string? Version,
    string? HostName,
    string? OperatingSystem,
    DateTimeOffset? LastSeenAt,
    string? Error,
    int Servers,
    DateTimeOffset CreatedAt)
{
    /// <summary>
    /// Whether it has been installed: an agent added on the Agents page has no key until its installer used a code.
    /// </summary>
    public bool Installed { get; init; } = true;

    /// <summary>When its open one-time code stops working; <see langword="null"/> when it has none.</summary>
    public DateTimeOffset? CodeExpiresAt { get; init; }

    /// <summary>Whether it reads its host's CPU, memory, disks, and network.</summary>
    public bool Host { get; init; }

    /// <summary>Whether it reads the Windows Event Log's errors.</summary>
    public bool EventLog { get; init; }

    /// <summary>Whether it reads IIS.</summary>
    public bool Iis { get; init; }

    /// <summary>Whether it reads the Docker containers on its Linux host.</summary>
    public bool Docker { get; init; }

    /// <summary>The environment its host belongs to, such as <c>production</c>.</summary>
    public string HostEnvironment { get; init; } = "production";
}
