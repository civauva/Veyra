namespace Veyra.Alerting;

/// <summary>A request to the Alerting module broke a rule, for example a webhook URL that is not HTTPS.</summary>
/// <remarks>The REST API answers it with <c>400 Bad Request</c> and the message as the problem's title.</remarks>
public sealed class AlertingValidationException : Exception
{
    /// <summary>Creates the exception without a message.</summary>
    public AlertingValidationException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, in words for the person who made the request.</param>
    public AlertingValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    /// <param name="message">What is wrong, in words for the person who made the request.</param>
    /// <param name="innerException">The exception that revealed it.</param>
    public AlertingValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
