namespace Veyra.Agents;

/// <summary>A request to the Agents module broke a rule, for example a port above 65535.</summary>
/// <remarks>The REST API answers it with <c>400 Bad Request</c> and the message as the problem's title.</remarks>
public sealed class AgentsValidationException : Exception
{
    /// <summary>Creates the exception without a message.</summary>
    public AgentsValidationException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, in words for the person who made the request.</param>
    public AgentsValidationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    /// <param name="message">What is wrong, in words for the person who made the request.</param>
    /// <param name="innerException">The exception that revealed it.</param>
    public AgentsValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
