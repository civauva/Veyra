namespace Veyra.Query.Search;

/// <summary>How well a name matches a search, best first (ADR 0022).</summary>
internal enum SearchRank
{
    /// <summary>The name is the text, in any case.</summary>
    Exact,

    /// <summary>The name starts with the text.</summary>
    Prefix,

    /// <summary>The name holds the text elsewhere.</summary>
    Contains,

    /// <summary>The name does not hold the text.</summary>
    None,
}
