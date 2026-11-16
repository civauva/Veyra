namespace Veyra.Core.Activity;

/// <summary>A program and machine that ran a statement, as the live view's reads saw it (ADR 0020).</summary>
/// <param name="Program">The client program.</param>
/// <param name="Host">The client machine.</param>
/// <param name="Login">The login.</param>
/// <param name="Samples">How many reads saw it run the statement.</param>
public sealed record SessionCaller(string Program, string Host, string Login, long Samples);
