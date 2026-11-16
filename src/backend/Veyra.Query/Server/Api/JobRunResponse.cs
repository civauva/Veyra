namespace Veyra.Query.Server.Api;

/// <summary>One run of a job.</summary>
/// <param name="At">When it started.</param>
/// <param name="Succeeded">Whether it succeeded.</param>
/// <param name="Seconds">How long it took.</param>
/// <param name="Message">SQL Server Agent's message.</param>
/// <param name="Steps">Its steps.</param>
public sealed record JobRunResponse(
    DateTimeOffset At,
    bool Succeeded,
    double Seconds,
    string Message,
    IReadOnlyList<JobStepResponse> Steps);
