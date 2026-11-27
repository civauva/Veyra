namespace Veyra.Access.Api;

/// <summary>The rings and the second-factor note of the Users screen, over everyone (ADR 0011, VEY-67).</summary>
/// <param name="People">Everyone, switched off or not.</param>
/// <param name="WithSecondFactor">People who use a second factor.</param>
/// <param name="RecentSignIns">People who signed in within the last 30 days.</param>
/// <param name="Admins">Admins who are not switched off.</param>
/// <param name="AdminsWithSecondFactor">Of those Admins, the ones with a second factor.</param>
/// <param name="Missing">
/// People who can sign in but have no second factor: those a rule requiring one would ask to set it up.
/// </param>
/// <param name="MissingAdmins">Of those, the Admins.</param>
public sealed record UserPostureResponse(
    int People,
    int WithSecondFactor,
    int RecentSignIns,
    int Admins,
    int AdminsWithSecondFactor,
    int Missing,
    int MissingAdmins);
