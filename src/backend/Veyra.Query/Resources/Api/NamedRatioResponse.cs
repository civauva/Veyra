namespace Veyra.Query.Resources.Api;

/// <summary>An item of a list and a share of it, such as the busiest host's CPU.</summary>
/// <param name="Name">The item's name.</param>
/// <param name="Ratio">The share, from 0 to 1; a container's CPU can pass 1 when it uses more than one CPU.</param>
public sealed record NamedRatioResponse(string Name, double Ratio);
