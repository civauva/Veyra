namespace Veyra.Query.Search.Api;

/// <summary>A root operation a search found, with how many traces it started in the window.</summary>
/// <param name="Kind">Always <c>operation</c>.</param>
/// <param name="Name">The root span's name, such as <c>POST /payments</c>.</param>
/// <param name="Service">The service that answered it.</param>
/// <param name="Count">Its traces in the window.</param>
/// <param name="Detail">The count in words, such as <c>18 in 24 h, 2 failed</c>.</param>
public sealed record SearchOperationResponse(string Kind, string Name, string Service, long Count, string Detail);
