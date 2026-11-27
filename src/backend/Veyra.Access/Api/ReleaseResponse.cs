namespace Veyra.Access.Api;

/// <summary>The notes of one release, as <c>CHANGELOG.md</c> has them (VEY-82).</summary>
/// <param name="Version">The release, such as <c>0.4.1</c>.</param>
/// <param name="Date">The day it was released, or null when the heading names none.</param>
/// <param name="Summary">A sentence on the release as a whole, or null.</param>
/// <param name="Sections">What changed, by kind.</param>
public sealed record ReleaseResponse(
    string Version,
    DateOnly? Date,
    string? Summary,
    IReadOnlyList<ReleaseSectionResponse> Sections);
