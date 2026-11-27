namespace Veyra.Access.Api;

/// <summary>One part of a release's notes, such as what was added.</summary>
/// <param name="Title">Its heading: Added, Changed, Fixed, or Removed.</param>
/// <param name="Items">Each change in a sentence.</param>
public sealed record ReleaseSectionResponse(string Title, IReadOnlyList<string> Items);
