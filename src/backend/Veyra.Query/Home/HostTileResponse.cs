namespace Veyra.Query.Home;

/// <summary>One host tile of the home (VEY-24, VEY-68).</summary>
/// <param name="Name">The host name.</param>
/// <param name="Health">Its worst state from its open alerts.</param>
/// <param name="Disk">
/// The disk it draws: the one that fills soonest within 30 days, or else its fullest; <see langword="null"/> without
/// disk metrics.
/// </param>
/// <param name="Filling">Whether that disk fills within 30 days, so the tile says when instead of how full.</param>
public sealed record HostTileResponse(string Name, BlockHealth Health, HomeDiskResponse? Disk, bool Filling);
