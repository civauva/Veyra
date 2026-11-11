namespace Veyra.Query.Home;

/// <summary>
/// One monitored thing on the home dashboard: a service seen in its logs, or a host with an open alert (ADR 0012).
/// </summary>
/// <param name="Id">Stable identity, for example <c>service:production/payments-gateway</c>.</param>
/// <param name="Kind">What it is: <c>service</c> or <c>host</c>.</param>
/// <param name="Name">The display name.</param>
/// <param name="Environment">Its environment, or null when the sender set none.</param>
/// <param name="Health">Its state, from its open alerts only.</param>
/// <param name="AlertIds">The open alerts that set the state.</param>
/// <param name="Records">Its records in the last five minutes; 0 for hosts.</param>
/// <param name="Errors">Its error records in the last five minutes; 0 for hosts.</param>
/// <param name="ErrorRatio">The share of its records that are errors, or null without records.</param>
public sealed record BlockResponse(
    string Id,
    string Kind,
    string Name,
    string? Environment,
    BlockHealth Health,
    IReadOnlyList<Guid> AlertIds,
    long Records,
    long Errors,
    double? ErrorRatio);
