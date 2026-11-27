using Veyra.Agents.Agents;
using Veyra.Agents.Api;
using Veyra.Agents.SqlServers;

namespace Veyra.Agents.Tests.Api;

/// <summary>The Settings list's counts of servers and agents come from the server (VEY-68).</summary>
public class AgentsSummaryTests
{
    [Fact]
    public void Of_ServersAndAgents_CountsThoseThatFailAndThoseOnline()
    {
        var summary = AgentsSummaryResponse.Of(
            [SqlServerState.Receiving, SqlServerState.LoginFailed, SqlServerState.WaitingForData],
            [SqlServerState.Unreachable],
            [AgentState.Online, AgentState.Offline, AgentState.Online]);

        Assert.Equal(new AgentsSummaryResponse(3, 1, 2, 3, 2), summary);
    }
}
