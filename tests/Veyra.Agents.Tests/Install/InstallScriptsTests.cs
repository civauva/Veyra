using Veyra.Agents.Install;

namespace Veyra.Agents.Tests.Install;

public class InstallScriptsTests
{
    private static readonly Uri _veyra = new("https://veyra.example.com/settings?section=Agents");

    [Fact]
    public void Windows_Script_HasVeyrasAddressTheVersionAndTheWindowsChecksums()
    {
        var script = InstallScripts.Windows(_veyra);

        Assert.Contains("$VeyraUrl = 'https://veyra.example.com'", script, StringComparison.Ordinal);
        Assert.Contains($"$VeyraAgentVersion = '{AgentRelease.Version}'", script, StringComparison.Ordinal);
        Assert.Contains(AgentRelease.SupervisorWindowsAmd64, script, StringComparison.Ordinal);
        Assert.Contains(AgentRelease.CollectorWindowsAmd64, script, StringComparison.Ordinal);
        Assert.Contains(@"$VeyraAgentPrograms = 'C:\Program Files\Veyra Agent'", script, StringComparison.Ordinal);
        Assert.Contains("function Install-VeyraAgent", script, StringComparison.Ordinal);
        Assert.Contains("function Uninstall-VeyraAgent", script, StringComparison.Ordinal);
        Assert.DoesNotMatch("__[A-Z0-9_]+__", script);
    }

    [Fact]
    public void Linux_Script_HasVeyrasAddressTheVersionAndBothArchitecturesChecksums()
    {
        var script = InstallScripts.Linux(_veyra);

        Assert.StartsWith("#!/bin/sh\n", script, StringComparison.Ordinal);
        Assert.Contains("VEYRA_URL='https://veyra.example.com'", script, StringComparison.Ordinal);
        Assert.Contains(AgentRelease.SupervisorLinuxAmd64, script, StringComparison.Ordinal);
        Assert.Contains(AgentRelease.CollectorLinuxArm64, script, StringComparison.Ordinal);
        Assert.Contains("PROGRAMS='/opt/veyra-agent'", script, StringComparison.Ordinal);
        Assert.DoesNotMatch("__[A-Z0-9_]+__", script);
        Assert.DoesNotContain('\r', script);
    }

    [Fact]
    public void Scripts_AddressWithADollarSign_IsRefused()
    {
        // A user name in the address is the one place an odd character can get through.
        var odd = new Uri("https://a$b@veyra.example.com");

        Assert.Throws<ArgumentException>(() => InstallScripts.Windows(odd));
    }

    [Theory]
    [InlineData(AgentRelease.SupervisorWindowsAmd64)]
    [InlineData(AgentRelease.CollectorWindowsAmd64)]
    [InlineData(AgentRelease.SupervisorLinuxAmd64)]
    [InlineData(AgentRelease.CollectorLinuxAmd64)]
    [InlineData(AgentRelease.SupervisorLinuxArm64)]
    [InlineData(AgentRelease.CollectorLinuxArm64)]
    public void Release_Checksums_AreSha256InHex(string sha256)
    {
        Assert.Matches("^[0-9a-f]{64}$", sha256);
    }
}
