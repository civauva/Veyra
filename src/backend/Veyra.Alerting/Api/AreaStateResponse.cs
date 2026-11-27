namespace Veyra.Alerting.Api;

/// <summary>How one area of a database server is, from its open alerts (VEY-62, VEY-68).</summary>
/// <param name="Area">The area, by its tab's name in the address, such as <c>blocking</c>.</param>
/// <param name="Health"><c>Warning</c>, <c>Caution</c>, or <c>Ok</c>, from its worst open alert.</param>
/// <param name="Alerts">Its open alerts, the warnings first.</param>
public sealed record AreaStateResponse(string Area, string Health, IReadOnlyList<AlertListItemResponse> Alerts);
