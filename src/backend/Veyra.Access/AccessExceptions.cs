namespace Veyra.Access;

/// <summary>
/// A request to the Access module was malformed, for example a password that is too short. The endpoints turn it into
/// <c>400 Bad Request</c>.
/// </summary>
/// <param name="message">What is wrong, in words that can be shown to the person.</param>
public sealed class AccessValidationException(string message) : Exception(message);

/// <summary>
/// A request to the Access module conflicts with the current state, for example an email that is already taken or
/// removing the last administrator. The endpoints turn it into <c>409 Conflict</c>.
/// </summary>
/// <param name="message">What conflicts, in words that can be shown to the person.</param>
public sealed class AccessConflictException(string message) : Exception(message);
