namespace Veyra.Agents;

/// <summary>A request to the Agents module clashes with what is stored, for example a server name in use.</summary>
/// <remarks>The REST API answers it with <c>409 Conflict</c> and the message as the problem's title.</remarks>
public sealed class AgentsConflictException : Exception
{
    /// <summary>Creates the exception without a message.</summary>
    public AgentsConflictException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, in words for the person who made the request.</param>
    public AgentsConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with its cause.</summary>
    /// <param name="message">What is wrong, in words for the person who made the request.</param>
    /// <param name="innerException">The exception that revealed it.</param>
    public AgentsConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
