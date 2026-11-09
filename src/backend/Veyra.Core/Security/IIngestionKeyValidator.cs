namespace Veyra.Core.Security;

/// <summary>
/// Checks the ingestion key that an OTLP sender presents. The Access module implements it; the Ingestion module
/// calls it for every request, without referencing the Access module.
/// </summary>
public interface IIngestionKeyValidator
{
    /// <summary>
    /// Checks a key taken from the <c>Authorization: Bearer</c> header.
    /// </summary>
    /// <remarks>
    /// Results are cached for a short time, so most calls do not touch the database. A revoked key therefore keeps
    /// working for at most that time, 30 seconds by default.
    /// </remarks>
    /// <param name="presentedKey">The key exactly as the sender sent it, without the <c>Bearer </c> prefix.</param>
    /// <param name="cancellationToken">Cancels the check when the request is aborted.</param>
    /// <returns>Whether the key is valid, and if so which key it is and where it may send data.</returns>
    ValueTask<IngestionKeyCheck> ValidateAsync(string presentedKey, CancellationToken cancellationToken);
}
