namespace Veyra.Query.Server.Api;

/// <summary>A server setting.</summary>
/// <param name="Name">The setting.</param>
/// <param name="Value">Its value in use.</param>
/// <param name="Recommended">What Veyra recommends.</param>
public sealed record SettingResponse(string Name, string Value, string Recommended);
