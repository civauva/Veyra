namespace Veyra.Query.Server.Api;

/// <summary>One step of a run.</summary>
/// <param name="Id">The step's number.</param>
/// <param name="Name">The step's name.</param>
/// <param name="Succeeded">Whether it succeeded.</param>
/// <param name="Seconds">How long it took.</param>
/// <param name="Message">Its message.</param>
public sealed record JobStepResponse(int Id, string Name, bool Succeeded, double Seconds, string Message);
