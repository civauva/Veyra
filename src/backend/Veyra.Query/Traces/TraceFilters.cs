namespace Veyra.Query.Traces;

/// <summary>The window and filters the trace endpoints share, bound from the query string.</summary>
public sealed class TraceFilters
{
    /// <summary>The start of the window; an hour before its end when omitted.</summary>
    public DateTimeOffset? From { get; set; }

    /// <summary>The end of the window; now when omitted.</summary>
    public DateTimeOffset? To { get; set; }

    /// <summary>Only traces whose request came to one of these services.</summary>
    public string[]? Service { get; set; }

    /// <summary>Only traces of these environments.</summary>
    public string[]? Environment { get; set; }

    /// <summary>Only traces whose request failed.</summary>
    public bool? Failed { get; set; }

    /// <summary>Only traces that took at least this many milliseconds.</summary>
    public double? MinDurationMs { get; set; }

    /// <summary>Only traces that took at most this many milliseconds.</summary>
    public double? MaxDurationMs { get; set; }

    /// <summary>Only traces whose name contains this, ignoring case.</summary>
    public string? Name { get; set; }

    /// <summary>Attribute filters of the root span, each <c>key:value</c>.</summary>
    public string[]? Attribute { get; set; }
}
