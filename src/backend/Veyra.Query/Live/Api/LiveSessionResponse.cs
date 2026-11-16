namespace Veyra.Query.Live.Api;

/// <summary>One busy session of a SQL Server in its live view (ADR 0020, area 3).</summary>
/// <param name="Id">SQL Server's session ID.</param>
/// <param name="Status">
/// <c>running</c>, <c>runnable</c>, or <c>suspended</c> for a request; <c>sleeping</c> for a session without one.
/// </param>
/// <param name="Command">The request's command, such as <c>UPDATE</c>; empty without a request.</param>
/// <param name="Database">The database it works in.</param>
/// <param name="Program">The client program.</param>
/// <param name="Host">The client machine.</param>
/// <param name="Login">The login.</param>
/// <param name="BlockedBy">The session it waits for, or <see langword="null"/>.</param>
/// <param name="Blocking">How many sessions wait for it, directly or through others.</param>
/// <param name="WaitType">
/// What it waits on, such as <c>LCK_M_U</c>; <see langword="null"/> when it does not wait.
/// </param>
/// <param name="WaitSeconds">How long it has waited on that.</param>
/// <param name="WaitResource">The resource it waits for; <see langword="null"/> when none.</param>
/// <param name="ElapsedSeconds">How long the request has run; 0 without a request.</param>
/// <param name="CpuSeconds">CPU time of the request, or of the session without one.</param>
/// <param name="Reads">Logical reads.</param>
/// <param name="Writes">Writes.</param>
/// <param name="TempdbBytes">Space it holds in tempdb.</param>
/// <param name="GrantBytes">Memory granted to the request.</param>
/// <param name="GrantRequestedBytes">Memory the request asked for.</param>
/// <param name="GrantWaiting">Whether the request still waits for its memory.</param>
/// <param name="TransactionSeconds">
/// How long its oldest transaction has been open; <see langword="null"/> without one.
/// </param>
/// <param name="ChangedData">Whether its open transaction changed data, and so holds locks others may need.</param>
/// <param name="OpenTransactions">How many transactions it has open.</param>
/// <param name="Isolation">Its isolation level in words, such as "read committed".</param>
/// <param name="PercentComplete">
/// How far a long operation has come, or <see langword="null"/> for other requests.
/// </param>
/// <param name="RemainingSeconds">SQL Server's estimate of the time a long operation still needs.</param>
/// <param name="QueryHash">The statement's query hash; <see langword="null"/> without a request.</param>
/// <param name="PlanHash">The statement's plan hash; <see langword="null"/> without a request.</param>
/// <param name="IdleSeconds">Time since its last request ended.</param>
/// <param name="ConnectedSeconds">Time since it logged in.</param>
/// <param name="Name">What the statement does, in words, such as "Update of Orders by CustomerId".</param>
/// <param name="Text">The statement running now; without a request, the last batch it sent.</param>
public sealed record LiveSessionResponse(
    int Id,
    string Status,
    string Command,
    string Database,
    string Program,
    string Host,
    string Login,
    int? BlockedBy,
    int Blocking,
    string? WaitType,
    double WaitSeconds,
    string? WaitResource,
    double ElapsedSeconds,
    double CpuSeconds,
    long Reads,
    long Writes,
    long TempdbBytes,
    long GrantBytes,
    long GrantRequestedBytes,
    bool GrantWaiting,
    double? TransactionSeconds,
    bool ChangedData,
    int OpenTransactions,
    string Isolation,
    double? PercentComplete,
    double RemainingSeconds,
    string? QueryHash,
    string? PlanHash,
    double IdleSeconds,
    double ConnectedSeconds,
    string Name,
    string Text);
