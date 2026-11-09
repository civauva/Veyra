using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Access.Keys;
using Veyra.Testing;

namespace Veyra.Access.Tests.Keys;

[Collection(PostgresGroup.Name)]
public class IngestionKeyServiceTests(PostgresFixture postgres)
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task CreateAsync_ValidRequest_ReturnsSecretOnceAndStoresOnlyItsHash()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);

        var created = await Service(database)
            .CreateAsync("web-01 collector", null, null, admin, CancellationToken.None);

        Assert.StartsWith(created.Key.DisplayPrefix, created.Secret, StringComparison.Ordinal);
        Assert.False(await AnyColumnContainsAsync(database, created.Secret));
    }

    [Fact]
    public async Task ValidateAsync_CreatedKey_ReturnsValidWithItsEnvironments()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);
        var created = await Service(database).CreateAsync("api", ["production"], null, admin, CancellationToken.None);

        var check = await Validator(database).ValidateAsync(created.Secret, CancellationToken.None);

        Assert.True(check.IsValid);
        Assert.Equal(created.Key.Id, check.KeyId);
        Assert.Equal(["production"], check.AllowedEnvironments);
    }

    [Fact]
    public async Task ValidateAsync_UnknownKey_ReturnsInvalid()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);

        var check = await Validator(database).ValidateAsync(IngestionKeyFormat.Generate(), CancellationToken.None);

        Assert.False(check.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RevokedKey_StopsWorkingWithinTheCacheDuration()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);
        var service = Service(database);
        var validator = Validator(database);
        var created = await service.CreateAsync("api", null, null, admin, CancellationToken.None);
        await validator.ValidateAsync(created.Secret, CancellationToken.None);

        await service.RevokeAsync(created.Key.Id, admin, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(31));

        Assert.False((await validator.ValidateAsync(created.Secret, CancellationToken.None)).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredKey_ReturnsInvalidEvenWhenCached()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);
        var expiresAt = _clock.GetUtcNow().AddSeconds(10);
        var created = await Service(database).CreateAsync("api", null, expiresAt, admin, CancellationToken.None);
        var validator = Validator(database);
        await validator.ValidateAsync(created.Secret, CancellationToken.None);

        _clock.Advance(TimeSpan.FromSeconds(11));

        Assert.False((await validator.ValidateAsync(created.Secret, CancellationToken.None)).IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ValidKey_RecordsWhenItWasLastUsed()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);
        var service = Service(database);
        var created = await service.CreateAsync("api", null, null, admin, CancellationToken.None);

        await Validator(database).ValidateAsync(created.Secret, CancellationToken.None);

        var listed = Assert.Single(await service.ListAsync(CancellationToken.None));
        Assert.Equal(_clock.GetUtcNow(), listed.LastUsedAt);
    }

    [Fact]
    public async Task RevokeAsync_UnknownKey_ReturnsFalse()
    {
        await using var database = await AccessDatabase.CreateAsync(postgres);
        var admin = await AccessDatabase.SeedAdminAsync(database);

        Assert.False(await Service(database).RevokeAsync(Guid.CreateVersion7(), admin, CancellationToken.None));
    }

    private IngestionKeyService Service(NpgsqlDataSource database) =>
        new(new IngestionKeyStore(database), _clock, NullLogger<IngestionKeyService>.Instance);

    private IngestionKeyValidator Validator(NpgsqlDataSource database) =>
        new(new IngestionKeyStore(database), _clock, Options.Create(new AccessOptions()));

    // Proves the secret is not stored anywhere in the key table, in any text form.
    private static async Task<bool> AnyColumnContainsAsync(NpgsqlDataSource database, string secret)
    {
        await using var command = database.CreateCommand(
            "SELECT count(*) FROM access.ingestion_key AS k WHERE position($1 IN k::text) > 0 OR key_hash = $2");
        command.Parameters.AddWithValue(secret);
        command.Parameters.AddWithValue(Encoding.UTF8.GetBytes(secret));

        return (long)(await command.ExecuteScalarAsync())! > 0;
    }
}
