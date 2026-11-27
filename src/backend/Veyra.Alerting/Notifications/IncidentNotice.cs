namespace Veyra.Alerting.Notifications;

/// <summary>The incident part of a notification, as receivers see it (ADR 0026).</summary>
/// <param name="Id">The incident's ID.</param>
/// <param name="Key">Its key, such as <c>INC-42</c>.</param>
/// <param name="Url">The incident's page in Veyra, when Veyra knows its public address.</param>
internal sealed record IncidentNotice(Guid Id, string Key, Uri? Url);
