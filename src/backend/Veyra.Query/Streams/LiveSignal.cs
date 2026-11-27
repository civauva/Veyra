namespace Veyra.Query.Streams;

/// <summary>What a live rate counts.</summary>
internal enum LiveSignal
{
    /// <summary>Log records.</summary>
    Logs,

    /// <summary>Spans.</summary>
    Spans,

    /// <summary>Metric points.</summary>
    Points,
}
