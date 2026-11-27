using Veyra.Agents.Probes;

namespace Veyra.Agents.Api;

/// <summary>Why a probe failed.</summary>
/// <param name="Reason">The reason, such as <c>login-failed</c>.</param>
/// <param name="Message">The error in words, as the agent or SQL Server gave it.</param>
public sealed record ProbeErrorResponse(ProbeFailure Reason, string Message);
