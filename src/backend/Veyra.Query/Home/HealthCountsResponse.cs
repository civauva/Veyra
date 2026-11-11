namespace Veyra.Query.Home;

/// <summary>
/// How many blocks are in each state, for the master lights.
/// </summary>
/// <param name="Warning">Blocks with a warning open.</param>
/// <param name="Caution">Blocks with a caution open and no warning.</param>
/// <param name="Ok">Blocks without an open alert.</param>
/// <param name="Unknown">Blocks whose data stopped arriving. Always 0 in this version.</param>
public sealed record HealthCountsResponse(int Warning, int Caution, int Ok, int Unknown);
