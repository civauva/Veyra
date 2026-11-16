namespace Veyra.Query.Storage.Api;

/// <summary>One data or log file of a database (ADR 0020, area 6).</summary>
/// <param name="Name">The file's logical name.</param>
/// <param name="Type"><c>data</c> or <c>log</c>.</param>
/// <param name="Volume">The volume it is on.</param>
/// <param name="SizeBytes">Its size.</param>
/// <param name="Growth">How it grows, such as <c>64 MB</c> or <c>10%</c>.</param>
/// <param name="MaxSize">Its maximum, such as <c>unlimited</c>.</param>
public sealed record DatabaseFileResponse(
    string Name,
    string Type,
    string Volume,
    double SizeBytes,
    string Growth,
    string MaxSize);
