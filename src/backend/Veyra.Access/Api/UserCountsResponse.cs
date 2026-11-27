namespace Veyra.Access.Api;

/// <summary>How many of the people that match the search each chip of the Users screen lists (VEY-67).</summary>
/// <param name="Everyone">Everyone that matches.</param>
/// <param name="NoFactor">Those without a second factor.</param>
/// <param name="Locked">Those locked out now.</param>
/// <param name="Off">Those switched off.</param>
public sealed record UserCountsResponse(int Everyone, int NoFactor, int Locked, int Off);
