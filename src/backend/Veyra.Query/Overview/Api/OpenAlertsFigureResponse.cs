namespace Veyra.Query.Overview.Api;

/// <summary>The alerts open now, counted as the home's needs attention counts them (ADR 0007).</summary>
/// <param name="Warning">The warnings.</param>
/// <param name="Caution">The cautions.</param>
public sealed record OpenAlertsFigureResponse(int Warning, int Caution);
