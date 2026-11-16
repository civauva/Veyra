using System.Globalization;

namespace Veyra.Agents.Agents;

/// <summary>
/// Writes the start-up file of the OpAMP supervisor that runs an agent's Collector. It holds only what the agent needs
/// before it reaches Veyra; Veyra sends the rest over OpAMP.
/// </summary>
/// <remarks>
/// The paths are where each way of installing puts the official programs: the agent image in
/// <c>deploy/agent/Dockerfile</c>, and Veyra's Windows and Linux installers.
/// </remarks>
internal static class SupervisorConfig
{
    /// <summary>Where the Windows installer puts the programs.</summary>
    public const string WindowsProgramFolder = @"C:\Program Files\Veyra Agent";

    /// <summary>Where the Windows installer keeps the start-up file and the supervisor's state.</summary>
    public const string WindowsDataFolder = @"C:\ProgramData\Veyra Agent";

    /// <summary>Where the Linux installer puts the programs.</summary>
    public const string LinuxProgramFolder = "/opt/veyra-agent";

    /// <summary>Where the Linux installer keeps the supervisor's state.</summary>
    public const string LinuxDataFolder = "/var/lib/veyra-agent";

    /// <summary>Writes the file's text.</summary>
    /// <param name="serverUrl">Veyra's OpAMP endpoint as the agent reaches it.</param>
    /// <param name="key">The agent's key.</param>
    /// <param name="platform">Where the agent's programs live.</param>
    /// <returns>The YAML text.</returns>
    public static string Write(Uri serverUrl, string key, AgentPlatform platform)
    {
        var (collector, storage) = platform switch
        {
            AgentPlatform.Windows => (
                $@"{WindowsProgramFolder}\otelcol-contrib.exe",
                $@"{WindowsDataFolder}\state"),
            AgentPlatform.Linux => ($"{LinuxProgramFolder}/otelcol-contrib", LinuxDataFolder),
            _ => ("/otelcol-contrib", "/var/lib/veyra-agent"),
        };

        // Single quotes keep Windows backslashes as they are.
        return string.Create(
            CultureInfo.InvariantCulture,
            $"""
            # Written by Veyra for this agent (ADR 0019). Veyra writes it again when the agent is installed again.
            server:
              endpoint: {serverUrl}
              headers:
                Authorization: Bearer {key}
            capabilities:
              accepts_remote_config: true
              reports_effective_config: true
              reports_remote_config: true
              reports_health: true
              reports_own_logs: true
              reports_own_metrics: false
            agent:
              executable: '{collector}'
              env:
                # SQL Server's own certificate has a negative serial number, which Go refuses by default (VEY-34).
                GODEBUG: x509negativeserial=1
            storage:
              directory: '{storage}'

            """);
    }
}
