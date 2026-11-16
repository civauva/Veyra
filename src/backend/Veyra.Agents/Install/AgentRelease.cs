namespace Veyra.Agents.Install;

/// <summary>
/// The release of the official OpenTelemetry programs that Veyra's installers put on a machine: the version Veyra
/// tests, and the SHA-256 of every file it downloads, from OpenTelemetry's published checksums.
/// </summary>
/// <remarks>
/// The values live in Veyra's code so an installer never trusts a checksum it fetched next to the file. Moving to a new
/// version means changing them here, in <c>deploy/agent/Dockerfile</c>, and in CI, together.
/// </remarks>
internal static class AgentRelease
{
    /// <summary>The version of both programs.</summary>
    public const string Version = "0.140.0";

    /// <summary>SHA-256 of <c>opampsupervisor_0.140.0_windows_amd64.exe</c>.</summary>
    public const string SupervisorWindowsAmd64 = "9cd15713fcae2889fb7bd0ebf9c19b7ced3f5bff3d54a69f3b3bd2dfaeb4b416";

    /// <summary>SHA-256 of <c>otelcol-contrib_0.140.0_windows_amd64.tar.gz</c>.</summary>
    public const string CollectorWindowsAmd64 = "86709bcc2cd7a0cddd0a0cfa583af4f25fa12432e3f0dee295740a9aa403d4e4";

    /// <summary>SHA-256 of <c>opampsupervisor_0.140.0_linux_amd64</c>.</summary>
    public const string SupervisorLinuxAmd64 = "497fa31f1dead871a38b4b1969abb85f41cf8949aa4596ba9b750306f7a1ff14";

    /// <summary>SHA-256 of <c>otelcol-contrib_0.140.0_linux_amd64.tar.gz</c>.</summary>
    public const string CollectorLinuxAmd64 = "4bacc689cadffd5b4d468a6ac67c7c3cb4ff07000533fe1cb8f5d8b5d557ac79";

    /// <summary>SHA-256 of <c>opampsupervisor_0.140.0_linux_arm64</c>.</summary>
    public const string SupervisorLinuxArm64 = "5252850d48eb16dda13b861ee4d4591633a9f97dfbde897103a681370e4807da";

    /// <summary>SHA-256 of <c>otelcol-contrib_0.140.0_linux_arm64.tar.gz</c>.</summary>
    public const string CollectorLinuxArm64 = "c20821064e838f2556b40ca73b071840e35b8fd296214c1868ffca4cf6602070";
}
