namespace Veyra.Query.Server.Api;

/// <summary>A log-shipped database.</summary>
/// <param name="Database">The database.</param>
/// <param name="Side"><c>primary</c> or <c>secondary</c>.</param>
/// <param name="Behind">Whether it is later than its threshold.</param>
/// <param name="DelaySeconds">Time since its last backup or restore, or <see langword="null"/>.</param>
public sealed record LogShippingResponse(string Database, string Side, bool Behind, double? DelaySeconds);
