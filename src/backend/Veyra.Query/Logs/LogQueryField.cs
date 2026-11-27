namespace Veyra.Query.Logs;

/// <summary>What a term of a log query filters on (ADR 0024).</summary>
internal enum LogQueryField
{
    /// <summary>A bare word or a quoted phrase, matched against the message.</summary>
    Text,

    /// <summary><c>service:</c>, the same as the <c>service</c> parameter.</summary>
    Service,

    /// <summary><c>env:</c> or <c>environment:</c>, the same as the <c>environment</c> parameter.</summary>
    Environment,

    /// <summary><c>host:</c>, the same as the <c>host</c> parameter.</summary>
    Host,

    /// <summary>
    /// <c>level:</c>, exactly that level, as the <c>level</c> parameter; or <c>level&gt;=</c>, that level or worse, as
    /// <c>severity</c>.
    /// </summary>
    Level,

    /// <summary><c>trace:</c>, the same as <c>traceId</c>.</summary>
    Trace,

    /// <summary><c>span:</c>, the same as <c>spanId</c>.</summary>
    Span,

    /// <summary>Any other <c>key:value</c>: a record attribute, the same as <c>attribute=key:value</c>.</summary>
    Attribute,
}
