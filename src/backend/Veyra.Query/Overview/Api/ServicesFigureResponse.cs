namespace Veyra.Query.Overview.Api;

/// <summary>How the services stand, as the Services list's header counts them (ADR 0022).</summary>
/// <param name="Healthy">How many have no open alert, silent ones included.</param>
/// <param name="Total">How many services are known.</param>
/// <param name="Warning">How many have a warning open.</param>
/// <param name="Caution">How many have a caution open and no warning.</param>
/// <param name="Silent">How many sent no metrics for longer than <c>Veyra:Query:SilentAfterServices</c>.</param>
public sealed record ServicesFigureResponse(int Healthy, int Total, int Warning, int Caution, int Silent);
