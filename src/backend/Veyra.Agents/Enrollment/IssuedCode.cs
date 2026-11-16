namespace Veyra.Agents.Enrollment;

/// <summary>A one-time code that installs an agent.</summary>
/// <param name="Code">The code. Only its hash is stored, so it is shown this once.</param>
/// <param name="ExpiresAt">When it stops working.</param>
public sealed record IssuedCode(string Code, DateTimeOffset ExpiresAt);
