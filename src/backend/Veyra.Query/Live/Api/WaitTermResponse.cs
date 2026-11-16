namespace Veyra.Query.Live.Api;

/// <summary>A wait type explained, for the Simple view's words and the popup that explains it (ADR 0020).</summary>
/// <param name="Type">The wait type, such as <c>LCK_M_U</c>.</param>
/// <param name="Category">Its category, such as Lock or Disk I/O.</param>
/// <param name="Simple">A few words for the Simple view, such as "waits for a lock".</param>
/// <param name="Meaning">What it means.</param>
/// <param name="Causes">What usually causes it.</param>
public sealed record WaitTermResponse(string Type, string Category, string Simple, string Meaning, string Causes);
