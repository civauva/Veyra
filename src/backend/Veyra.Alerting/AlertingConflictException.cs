namespace Veyra.Alerting;

/// <summary>A request to the Alerting module clashes with what is stored, for example a channel name in use.</summary>
/// <remarks>The REST API answers it with <c>409 Conflict</c> and the message as the problem's title.</remarks>
public sealed class AlertingConflictException : Exception
{
    /// <summary>Creates the exception without a message.</summary>
    public AlertingConflictException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What clashes, in words for the person who made the request.</param>
    public AlertingConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    /// <param name="message">What clashes, in words for the person who made the request.</param>
    /// <param name="innerException">The exception that revealed it.</param>
    public AlertingConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
