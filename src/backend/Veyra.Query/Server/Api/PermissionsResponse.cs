namespace Veyra.Query.Server.Api;

/// <summary>
/// What the monitoring login may not read on a server, so each tab can say why it is empty and how to fix it (VEY-58).
/// </summary>
/// <param name="Reported">
/// Whether the agent reported the login's permissions within the last 15 minutes; an agent on an older template does
/// not.
/// </param>
/// <param name="Login">The login the agent signs in as, or <see langword="null"/> when nothing was reported.</param>
/// <param name="Platform">
/// What the agent reads: <c>sqlserver</c>, <c>managed-instance</c>, or <c>azure-sql-database</c> (VEY-57); the screen
/// leaves out what that platform does not have. <see langword="null"/> when nothing was reported.
/// </param>
/// <param name="Gaps">Each missing permission with the tabs it leaves empty, in the order of the script.</param>
/// <param name="Script">The statements that grant every missing permission, or <see langword="null"/> for none.</param>
public sealed record PermissionsResponse(
    bool Reported,
    string? Login,
    string? Platform,
    IReadOnlyList<PermissionGapResponse> Gaps,
    string? Script);
