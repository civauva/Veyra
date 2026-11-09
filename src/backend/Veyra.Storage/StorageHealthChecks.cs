namespace Veyra.Storage;

/// <summary>
/// Names shared between the Storage module's health checks and the endpoints that report them.
/// </summary>
public static class StorageHealthChecks
{
    /// <summary>
    /// The tag of checks that must pass before an instance receives traffic. <c>/readyz</c> runs only these checks.
    /// </summary>
    public const string ReadinessTag = "ready";
}
