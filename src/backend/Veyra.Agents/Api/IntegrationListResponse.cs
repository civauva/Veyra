namespace Veyra.Agents.Api;

/// <summary>Every integration, as the Integrations screen lists them (ADR 0028).</summary>
/// <param name="Items">The integrations, by name.</param>
public sealed record IntegrationListResponse(IReadOnlyList<IntegrationItemResponse> Items);
