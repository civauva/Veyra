namespace Veyra.Alerting.Api;

/// <summary>A channel an alert told (VEY-68).</summary>
/// <param name="Id">The channel's ID.</param>
/// <param name="Name">Its name.</param>
public sealed record ToldChannelResponse(Guid Id, string Name);
