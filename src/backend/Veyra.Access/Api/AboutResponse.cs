namespace Veyra.Access.Api;

/// <summary>The Veyra that answers (VEY-82).</summary>
/// <param name="Version">Its release, such as <c>0.4.1</c>.</param>
/// <param name="Build">
/// Which build of the app it serves. An open page that loaded another build offers to reload; the value means nothing
/// else.
/// </param>
public sealed record AboutResponse(string Version, string Build);
