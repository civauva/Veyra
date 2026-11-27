namespace Veyra.Query.Resources.Api;

/// <summary>A database without a recent full backup.</summary>
/// <param name="Server">Its server.</param>
/// <param name="Database">Its name.</param>
public sealed record LateBackupResponse(string Server, string Database);
