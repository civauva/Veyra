using Npgsql;
using Veyra.Alerting.Notifications;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.Notifications;

[Collection(PostgresGroup.Name)]
public class ChannelStoreTests(PostgresFixture postgres)
{
    [Fact]
    public async Task InsertAsync_ThenGet_ReturnsEveryField()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new ChannelStore(database);
        var email = Channels.Email("on-call", "a@example.com", "b@example.com") with { SendCaution = false };

        await store.InsertAsync(email, CancellationToken.None);

        var stored = await store.GetAsync(email.Id, CancellationToken.None);

        // Records compare lists by reference, so the recipients are compared on their own.
        Assert.Equal(email with { EmailRecipients = null }, stored! with { EmailRecipients = null });
        Assert.Equal(["a@example.com", "b@example.com"], stored.EmailRecipients);
    }

    [Fact]
    public async Task InsertAsync_NameTakenIgnoringCase_Throws()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new ChannelStore(database);
        await store.InsertAsync(Channels.Webhook("Ops"), CancellationToken.None);

        var thrown = await Assert.ThrowsAsync<PostgresException>(
            () => store.InsertAsync(Channels.Webhook("ops"), CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, thrown.SqlState);
    }

    [Fact]
    public async Task UpdateAsync_ChangesEverythingButTheCreationTime()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new ChannelStore(database);
        var channel = await store.InsertAsync(Channels.Webhook("ops"), CancellationToken.None);
        var changed = channel with
        {
            Name = "ops-alerts",
            Enabled = false,
            WebhookUrlHint = "https://other.example.com/…",
            CreatedAt = channel.CreatedAt.AddDays(5),
            UpdatedAt = channel.UpdatedAt.AddDays(1),
        };

        Assert.True(await store.UpdateAsync(changed, CancellationToken.None));

        Assert.Equal(changed with { CreatedAt = channel.CreatedAt }, await store.GetAsync(channel.Id, default));
    }

    [Fact]
    public async Task DeleteAsync_RemovesItAndListOrdersByName()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var store = new ChannelStore(database);
        var gone = await store.InsertAsync(Channels.Webhook("middle"), CancellationToken.None);
        await store.InsertAsync(Channels.Webhook("Zed"), CancellationToken.None);
        await store.InsertAsync(Channels.Email("alpha"), CancellationToken.None);

        Assert.True(await store.DeleteAsync(gone.Id, CancellationToken.None));
        Assert.False(await store.DeleteAsync(gone.Id, CancellationToken.None));

        Assert.Equal(["alpha", "Zed"], (await store.ListAsync(CancellationToken.None)).Select(c => c.Name));
    }
}
