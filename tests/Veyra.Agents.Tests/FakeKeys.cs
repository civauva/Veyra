using Veyra.Core.Security;

namespace Veyra.Agents.Tests;

/// <summary>Issues and checks keys in memory, as the Access module does in the database.</summary>
internal sealed class FakeKeys : IIngestionKeyIssuer, IIngestionKeyValidator
{
    public List<IssuedIngestionKey> Issued { get; } = [];

    public List<Guid> Revoked { get; } = [];

    public Task<IssuedIngestionKey> IssueAsync(string name, CancellationToken cancellationToken)
    {
        var key = new IssuedIngestionKey(Guid.CreateVersion7(), $"vyk_{Guid.NewGuid():N}");
        Issued.Add(key);

        return Task.FromResult(key);
    }

    public Task RevokeAsync(Guid keyId, CancellationToken cancellationToken)
    {
        Revoked.Add(keyId);

        return Task.CompletedTask;
    }

    public ValueTask<IngestionKeyCheck> ValidateAsync(string presentedKey, CancellationToken cancellationToken) =>
        ValueTask.FromResult(
            Issued.FirstOrDefault(key => key.Secret == presentedKey) is { } key && !Revoked.Contains(key.KeyId)
                ? new IngestionKeyCheck(true, key.KeyId, null)
                : IngestionKeyCheck.Invalid);
}
