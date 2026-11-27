namespace Veyra.Query.Systems.Api;

/// <summary>
/// What a list left out because the server's own rows were not asked for (VEY-66), so the screen can say so and
/// offer to show them without filtering anything itself.
/// </summary>
/// <param name="Count">How many rows were left out.</param>
/// <param name="Names">
/// The names among them an alert can be about, such as <c>msdb</c> or a job's name, so the screen can say that a hidden
/// one has an alert open. At most 20, in order.
/// </param>
public sealed record HiddenRowsResponse(int Count, IReadOnlyList<string> Names)
{
    /// <summary>Nothing left out, as when the server's own rows are shown.</summary>
    public static HiddenRowsResponse None { get; } = new(0, []);
}
