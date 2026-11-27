namespace Veyra.Core.Search;

/// <summary>A root operation whose name matched a search, with its traces in the window.</summary>
/// <param name="Name">The root span's name, such as <c>POST /payments</c>.</param>
/// <param name="Service">The service whose root span it is.</param>
/// <param name="Count">Its traces in the window.</param>
/// <param name="Failed">How many of them failed: the root span has error status.</param>
public sealed record FoundOperation(string Name, string Service, long Count, long Failed);
