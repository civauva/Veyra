namespace Veyra.Query.Home;

/// <summary>The home's disk outlook (VEY-68).</summary>
/// <param name="Filling">The disks that fill within 30 days and have no open alert, the soonest first.</param>
/// <param name="Hosts">How many hosts report disks.</param>
/// <param name="AllAlerted">
/// Whether disks fill within 30 days but every one already has an open alert, which Needs you tells instead.
/// </param>
public sealed record HomeDiskOutlookResponse(IReadOnlyList<HomeDiskResponse> Filling, int Hosts, bool AllAlerted);
