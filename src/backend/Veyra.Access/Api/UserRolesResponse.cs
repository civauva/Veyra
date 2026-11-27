namespace Veyra.Access.Api;

/// <summary>How many people hold each role, switched off or not, for the Users screen's role bar (VEY-67).</summary>
/// <param name="Admins">People with the Admin role.</param>
/// <param name="Editors">People with the Editor role.</param>
/// <param name="Viewers">People with the Viewer role.</param>
public sealed record UserRolesResponse(int Admins, int Editors, int Viewers);
