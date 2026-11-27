namespace Veyra.Query.Overview.Api;

/// <summary>The hosts, as the Hosts list's header counts them (ADR 0022).</summary>
/// <param name="Online">How many of the known hosts are not silent.</param>
/// <param name="Known">How many hosts are known: every host whose metrics are kept, silent ones too.</param>
public sealed record HostsFigureResponse(int Online, int Known);
