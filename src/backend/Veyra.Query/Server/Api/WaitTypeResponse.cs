using Veyra.Query.Live.Api;

namespace Veyra.Query.Server.Api;

/// <summary>A wait type over the range, explained (ADR 0020, area 2).</summary>
/// <param name="Term">The type explained.</param>
/// <param name="Seconds">Seconds waited on it over the range, by every session together.</param>
public sealed record WaitTypeResponse(WaitTermResponse Term, double Seconds);
