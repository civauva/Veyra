namespace Veyra.Agents.Api;

/// <summary>The template version to move an integration to: Update or Roll back (ADR 0028).</summary>
/// <param name="Version">The version, such as <c>0.1.0</c>.</param>
public sealed record VersionRequest(string Version);
