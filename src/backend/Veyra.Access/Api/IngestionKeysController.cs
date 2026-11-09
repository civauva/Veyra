using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Veyra.Access.Keys;
using Veyra.Access.Sessions;
using Veyra.Core.Security;

namespace Veyra.Access.Api;

/// <summary>
/// Manages the ingestion keys that OTLP senders present. Administrators only (ADR 0008).
/// </summary>
[ApiController]
[Route("api/v1/ingestion-keys")]
[Authorize(Policy = VeyraPolicies.Admin)]
[AccessErrors]
public sealed class IngestionKeysController : ControllerBase
{
    private readonly IngestionKeyService _keys;

    internal IngestionKeysController(IngestionKeyService keys)
    {
        _keys = keys;
    }

    /// <summary>Lists all keys, newest first, including revoked ones. Secrets are never included.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The keys.</returns>
    [HttpGet]
    public async Task<IEnumerable<IngestionKeyResponse>> ListAsync(CancellationToken cancellationToken) =>
        (await _keys.ListAsync(cancellationToken)).Select(key => new IngestionKeyResponse(
            key.Id,
            key.Name,
            key.DisplayPrefix,
            key.Environments,
            key.CreatedAt,
            key.ExpiresAt,
            key.RevokedAt,
            key.LastUsedAt));

    /// <summary>Creates a key. The response holds its secret, which is never shown again.</summary>
    /// <param name="request">What the key is for, and its optional limits.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The key with its secret.</returns>
    /// <response code="201">The key was created. Copy the secret now.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<CreatedIngestionKeyResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateAsync(CreateIngestionKeyRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = await _keys.CreateAsync(
            request.Name,
            request.Environments,
            request.ExpiresAt,
            User.UserId(),
            cancellationToken);

        var key = created.Key;

        return Created(
            $"/api/v1/ingestion-keys/{key.Id}",
            new CreatedIngestionKeyResponse(
                key.Id,
                key.Name,
                key.DisplayPrefix,
                key.Environments,
                key.ExpiresAt,
                created.Secret));
    }

    /// <summary>Revokes a key. Senders using it are rejected within 30 seconds.</summary>
    /// <param name="id">The key's ID.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>No content.</returns>
    /// <response code="204">The key was revoked.</response>
    /// <response code="404">No active key has this ID.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeAsync(Guid id, CancellationToken cancellationToken) =>
        await _keys.RevokeAsync(id, User.UserId(), cancellationToken) ? NoContent() : NotFound();
}
