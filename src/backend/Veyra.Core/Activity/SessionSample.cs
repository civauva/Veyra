namespace Veyra.Core.Activity;

/// <summary>
/// One session of a SQL Server as one read of the live view saw it: a session that ran a request, blocked another,
/// waited, held a transaction open, or used much of tempdb (ADR 0020, area 3).
/// </summary>
/// <param name="Block">The server's block.</param>
/// <param name="Environment">The server's environment.</param>
/// <param name="SampledAt">When the server was read.</param>
/// <param name="SessionId">SQL Server's session ID.</param>
/// <param name="RequestStatus">
/// The request's status, such as <c>running</c> or <c>suspended</c>; empty when idle.
/// </param>
/// <param name="SessionStatus">The session's status, such as <c>running</c> or <c>sleeping</c>.</param>
/// <param name="Command">
/// The request's command, such as <c>UPDATE</c> or <c>BACKUP DATABASE</c>; empty when idle.
/// </param>
/// <param name="Database">The database the request or session is in.</param>
/// <param name="Program">The client program's name, as it connected.</param>
/// <param name="Host">The client machine's name, as it connected.</param>
/// <param name="Login">The login.</param>
/// <param name="BlockedBy">The session this one waits for, or 0.</param>
/// <param name="WaitType">What the request waits on now, such as <c>LCK_M_U</c>; empty when it does not wait.</param>
/// <param name="WaitMilliseconds">How long it has waited on that.</param>
/// <param name="WaitResource">The resource it waits for, such as a key lock; empty when none.</param>
/// <param name="ElapsedMilliseconds">How long the request has run; 0 when idle.</param>
/// <param name="CpuMilliseconds">CPU time of the request, or of the session when idle.</param>
/// <param name="Reads">Logical reads of the request, or of the session when idle.</param>
/// <param name="Writes">Writes of the request, or of the session when idle.</param>
/// <param name="TempdbKilobytes">Space the session holds in tempdb.</param>
/// <param name="GrantKilobytes">The memory granted to the request.</param>
/// <param name="GrantRequestedKilobytes">The memory the request asked for.</param>
/// <param name="GrantWaiting">Whether the request still waits for its memory.</param>
/// <param name="TransactionMilliseconds">
/// How long the session's oldest open transaction has been open; <see langword="null"/> when it has none.
/// </param>
/// <param name="TransactionLogRecords">
/// Log records the open transaction wrote: more than 0 means it changed data.
/// </param>
/// <param name="OpenTransactions">How many transactions the session has open.</param>
/// <param name="Isolation">The isolation level, as SQL Server numbers it: 1 read uncommitted to 5 snapshot.</param>
/// <param name="PercentComplete">How far a long operation, such as a backup, has come, from 0 to 100.</param>
/// <param name="RemainingMilliseconds">SQL Server's estimate of the time a long operation still needs.</param>
/// <param name="QueryHash">The statement's query hash, such as <c>0x6ACBDEE9D5014909</c>; empty when idle.</param>
/// <param name="PlanHash">The statement's plan hash; empty when idle.</param>
/// <param name="IdleMilliseconds">Time since the session's last request ended.</param>
/// <param name="ConnectedSeconds">Time since the session logged in.</param>
/// <param name="Text">
/// The statement running now; for a session without a request, the last batch it sent. Up to 4,000 characters.
/// </param>
/// <param name="Running">Whether the session runs a request now.</param>
/// <param name="Blocking">Whether another session waits for this one.</param>
public sealed record SessionSample(
    string Block,
    string? Environment,
    DateTimeOffset SampledAt,
    int SessionId,
    string RequestStatus,
    string SessionStatus,
    string Command,
    string Database,
    string Program,
    string Host,
    string Login,
    int BlockedBy,
    string WaitType,
    long WaitMilliseconds,
    string WaitResource,
    long ElapsedMilliseconds,
    long CpuMilliseconds,
    long Reads,
    long Writes,
    long TempdbKilobytes,
    long GrantKilobytes,
    long GrantRequestedKilobytes,
    bool GrantWaiting,
    long? TransactionMilliseconds,
    long TransactionLogRecords,
    int OpenTransactions,
    int Isolation,
    double PercentComplete,
    long RemainingMilliseconds,
    string QueryHash,
    string PlanHash,
    long IdleMilliseconds,
    long ConnectedSeconds,
    string Text,
    bool Running,
    bool Blocking) : DatabaseActivity(Block, Environment, SampledAt);
