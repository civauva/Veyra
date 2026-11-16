using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Access.Keys;
using Veyra.Testing;

namespace Veyra.Access.Tests.Keys;

[Collection(PostgresGroup.Name)]
public class IngestionKeyIssuerTests(PostgresFixture postgres)
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task IssueAsync_WithoutAnAdmin_StoresAKeyThatValidates()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);

        var issued = await Issuer(database).IssueAsync("Agent: built in", CancellationToken.None);

        var check = await Validator(database).ValidateAsync(issued.Secret, CancellationToken.None);
        Assert.True(check.IsValid);
        Assert.Equal(issued.KeyId, check.KeyId);
        Assert.Null(check.AllowedEnvironments);
    }

    [Fact]
    public async Task IssueAsync_Key_IsListedWithoutACreator()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);

        var issued = await Issuer(database).IssueAsync("Agent: built in", CancellationToken.None);

        var key = Assert.Single(await Service(database).ListAsync(CancellationToken.None));
        Assert.Equal(issued.KeyId, key.Id);
        Assert.Equal("Agent: built in", key.Name);
        Assert.Null(key.CreatedBy);
    }

    [Fact]
    public async Task RevokeAsync_IssuedKey_StopsValidating()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);
        var issuer = Issuer(database);
        var issued = await issuer.IssueAsync("Agent: built in", CancellationToken.None);

        await issuer.RevokeAsync(issued.KeyId, CancellationToken.None);

        Assert.False((await Validator(database).ValidateAsync(issued.Secret, CancellationToken.None)).IsValid);
    }

    private IngestionKeyIssuer Issuer(NpgsqlDataSource database) => new(Service(database));

    private IngestionKeyService Service(NpgsqlDataSource database) =>
        new(new IngestionKeyStore(database), _clock, NullLogger<IngestionKeyService>.Instance);

    private IngestionKeyValidator Validator(NpgsqlDataSource database) =>
        new(new IngestionKeyStore(database), _clock, new TestOptionsMonitor<AccessOptions>(new AccessOptions()));
}
