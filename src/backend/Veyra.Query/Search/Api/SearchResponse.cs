namespace Veyra.Query.Search.Api;

/// <summary>What a search found, in the fixed order of its groups (ADR 0022).</summary>
/// <param name="Query">The text searched for, trimmed.</param>
/// <param name="Services">Up to five services.</param>
/// <param name="Hosts">Up to five hosts and containers.</param>
/// <param name="Databases">Up to five database servers and databases.</param>
/// <param name="Traces">Up to five root operations, with their traces in the window.</param>
/// <param name="Logs">The number of records whose message holds the words.</param>
public sealed record SearchResponse(
    string Query,
    SearchGroupResponse<SearchItemResponse> Services,
    SearchGroupResponse<SearchItemResponse> Hosts,
    SearchGroupResponse<SearchItemResponse> Databases,
    SearchGroupResponse<SearchOperationResponse> Traces,
    SearchCountResponse Logs)
{
    /// <summary>The answer to an empty search, which finds nothing and reads nothing.</summary>
    /// <param name="query">The text, empty or white space only.</param>
    /// <returns>Every group empty and complete.</returns>
    public static SearchResponse Empty(string query) => new(
        query,
        new SearchGroupResponse<SearchItemResponse>([], false),
        new SearchGroupResponse<SearchItemResponse>([], false),
        new SearchGroupResponse<SearchItemResponse>([], false),
        new SearchGroupResponse<SearchOperationResponse>([], false),
        new SearchCountResponse(0, false));
}
