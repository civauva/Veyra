namespace Veyra.Query.Blocking.Api;

/// <summary>One session of a deadlock (ADR 0020, area 5).</summary>
/// <param name="Id">The process's ID in the graph.</param>
/// <param name="Session">SQL Server's session ID.</param>
/// <param name="Program">The client program.</param>
/// <param name="Host">The client machine.</param>
/// <param name="Login">The login.</param>
/// <param name="Database">The database.</param>
/// <param name="Victim">Whether SQL Server ended it to break the deadlock.</param>
/// <param name="Waits">The lock it waited for, such as <c>U</c> on a key.</param>
/// <param name="Name">What its statement does, in words.</param>
/// <param name="Text">Its statement.</param>
public sealed record DeadlockProcessResponse(
    string Id,
    int Session,
    string Program,
    string Host,
    string Login,
    string Database,
    bool Victim,
    string Waits,
    string Name,
    string Text);
