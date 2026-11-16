using Veyra.Agents.Agents;

namespace Veyra.Agents.Install;

/// <summary>
/// Veyra's installers for agents in other networks (VEY-40): a PowerShell script for Windows and a shell script for
/// Linux, embedded in the module and served with Veyra's address, the pinned version, and its checksums filled in.
/// </summary>
internal static class InstallScripts
{
    private static readonly Lazy<string> _windows = new(() => Load("install.ps1"));

    private static readonly Lazy<string> _linux = new(() => Load("install.sh"));

    /// <summary>The Windows installer.</summary>
    /// <param name="veyraUrl">Veyra's address as the machine reaches it.</param>
    /// <returns>The PowerShell script.</returns>
    public static string Windows(Uri veyraUrl) => Fill(_windows.Value, veyraUrl);

    /// <summary>The Linux installer.</summary>
    /// <param name="veyraUrl">Veyra's address as the machine reaches it.</param>
    /// <returns>The shell script.</returns>
    public static string Linux(Uri veyraUrl) => Fill(_linux.Value, veyraUrl);

    private static string Fill(string script, Uri veyraUrl)
    {
        ArgumentNullException.ThrowIfNull(veyraUrl);

        // Only the scheme, host, and port: the scripts add the paths. A quote or a dollar sign could break out of the
        // scripts' quoted strings, so an address with either is refused rather than filled in.
        var address = veyraUrl.GetLeftPart(UriPartial.Authority);

        if (address.IndexOfAny(['\'', '"', '$', '`']) >= 0)
        {
            throw new ArgumentException("Veyra's address holds characters a script cannot quote.", nameof(veyraUrl));
        }

        return script
            .Replace("__VEYRA_URL__", address, StringComparison.Ordinal)
            .Replace("__VERSION__", AgentRelease.Version, StringComparison.Ordinal)
            .Replace("__SUPERVISOR_WINDOWS_AMD64__", AgentRelease.SupervisorWindowsAmd64, StringComparison.Ordinal)
            .Replace("__COLLECTOR_WINDOWS_AMD64__", AgentRelease.CollectorWindowsAmd64, StringComparison.Ordinal)
            .Replace("__SUPERVISOR_LINUX_AMD64__", AgentRelease.SupervisorLinuxAmd64, StringComparison.Ordinal)
            .Replace("__COLLECTOR_LINUX_AMD64__", AgentRelease.CollectorLinuxAmd64, StringComparison.Ordinal)
            .Replace("__SUPERVISOR_LINUX_ARM64__", AgentRelease.SupervisorLinuxArm64, StringComparison.Ordinal)
            .Replace("__COLLECTOR_LINUX_ARM64__", AgentRelease.CollectorLinuxArm64, StringComparison.Ordinal)
            .Replace("__WINDOWS_PROGRAMS__", SupervisorConfig.WindowsProgramFolder, StringComparison.Ordinal)
            .Replace("__WINDOWS_DATA__", SupervisorConfig.WindowsDataFolder, StringComparison.Ordinal)
            .Replace("__LINUX_PROGRAMS__", SupervisorConfig.LinuxProgramFolder, StringComparison.Ordinal)
            .Replace("__LINUX_DATA__", SupervisorConfig.LinuxDataFolder, StringComparison.Ordinal);
    }

    private static string Load(string name)
    {
        var resource = $"Veyra.Agents.Install.{name}";

        using var stream = typeof(InstallScripts).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded resource '{resource}' is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
