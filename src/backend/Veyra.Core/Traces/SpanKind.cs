namespace Veyra.Core.Traces;

/// <summary>What a span does in its trace, as OpenTelemetry defines it; the numbers are the OTLP values.</summary>
public enum SpanKind
{
    /// <summary>The sender did not say.</summary>
    Unspecified = 0,

    /// <summary>Work inside a service, for example a method or a step of a job.</summary>
    Internal = 1,

    /// <summary>Handling a request that came from another service.</summary>
    Server = 2,

    /// <summary>A request to another service or a database.</summary>
    Client = 3,

    /// <summary>Sending a message for another service to handle later.</summary>
    Producer = 4,

    /// <summary>Handling a message another service sent.</summary>
    Consumer = 5,
}
