namespace Veyra.Access.Users;

/// <summary>One day of a person's sign-in attempts on their own calendar, for the person panel's chart (VEY-68).</summary>
/// <param name="Day">The day, in the time zone the person sees times in.</param>
/// <param name="SignedIn">Attempts that signed in, a recovery code included.</param>
/// <param name="Failed">Attempts that did not.</param>
internal sealed record SignInDay(DateOnly Day, int SignedIn, int Failed);
