using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Veyra.Alerting.Notifications;
using Veyra.Testing;

namespace Veyra.Alerting.Tests.Notifications;

[Collection(PostgresGroup.Name)]
public sealed class ChannelServiceTests(PostgresFixture postgres) : IDisposable
{
    private const string _url = "https://hooks.slack.com/services/T0A1/B0C2/x9Y";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 10, 7, 0, 0, TimeSpan.Zero));
    private readonly FakeWebhooks _webhooks = new();
    private readonly EphemeralDataProtectionProvider _keys = new();

    [Fact]
    public async Task CreateAsync_Webhook_StoresTheUrlEncryptedWithAHint()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);

        var channel = await Service(database).CreateAsync(Webhook(), CancellationToken.None);

        var stored = await new ChannelStore(database).GetAsync(channel.Id, default);
        Assert.Equal("#ops-alerts", stored!.Name);
        Assert.Equal("https://hooks.slack.com/services/T0…", stored.WebhookUrlHint);
        Assert.DoesNotContain("hooks.slack.com", stored.WebhookUrlProtected, StringComparison.Ordinal);
        Assert.Equal(new Uri(_url), new WebhookUrlProtector(_keys).Unprotect(stored.WebhookUrlProtected!));
        Assert.Equal(_clock.GetUtcNow(), stored.CreatedAt);
    }

    [Fact]
    public async Task CreateAsync_NameTaken_IsAConflict()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var service = Service(database);
        await service.CreateAsync(Webhook(), CancellationToken.None);

        await Assert.ThrowsAsync<AlertingConflictException>(
            () => service.CreateAsync(Webhook() with { Name = "#OPS-ALERTS" }, CancellationToken.None));
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("#ops", "warning, caution, or both")]
    public async Task CreateAsync_BrokenRule_IsRefused(string name, string expected)
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var input = Webhook() with { Name = name, SendWarning = name.Length == 0, SendCaution = false };

        var thrown = await Assert.ThrowsAsync<AlertingValidationException>(
            () => Service(database).CreateAsync(input, CancellationToken.None));

        Assert.Contains(expected, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_Email_KeepsDistinctTrimmedAddresses()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);

        var channel = await Service(database).CreateAsync(
            Email(" oncall@example.com", "lead@example.com", "ONCALL@example.com", ""),
            CancellationToken.None);

        Assert.Equal(["oncall@example.com", "lead@example.com"], channel.EmailRecipients);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("Lead <lead@example.com>")]
    [InlineData("a@example.com, b@example.com")]
    public async Task CreateAsync_EmailThatIsNotOneBareAddress_IsRefused(string entry)
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);

        await Assert.ThrowsAsync<AlertingValidationException>(
            () => Service(database).CreateAsync(Email(entry), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_WebhookWithoutUrl_KeepsTheStoredOne()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var service = Service(database);
        var created = await service.CreateAsync(Webhook(), CancellationToken.None);
        _clock.Advance(TimeSpan.FromHours(1));

        var updated = await service.UpdateAsync(
            created.Id,
            Webhook() with { Name = "ops", WebhookUrl = null, Enabled = false },
            CancellationToken.None);

        Assert.Equal(created.WebhookUrlProtected, updated!.WebhookUrlProtected);
        Assert.Equal(("ops", false), (updated.Name, updated.Enabled));
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.Equal(_clock.GetUtcNow(), updated.UpdatedAt);
    }

    [Fact]
    public async Task UpdateAsync_UnknownChannel_ReturnsNull()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);

        Assert.Null(await Service(database).UpdateAsync(Guid.CreateVersion7(), Webhook(), CancellationToken.None));
    }

    [Fact]
    public async Task TestAsync_SendsATestMessageEvenWhenSwitchedOff()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var service = Service(database);
        var channel = await service.CreateAsync(Webhook() with { Enabled = false }, CancellationToken.None);

        var result = await service.TestAsync(channel.Id, CancellationToken.None);

        Assert.True(result!.Succeeded);
        var (request, body) = Assert.Single(_webhooks.Received);
        Assert.Equal(new Uri(_url), request.RequestUri);
        Assert.Contains("\"event\":\"test\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestDraftAsync_ChangedWebhookWithoutUrl_UsesTheStoredUrl()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var service = Service(database);
        var channel = await service.CreateAsync(Webhook(), CancellationToken.None);

        var result = await service.TestDraftAsync(
            Webhook() with { Name = "renamed", WebhookUrl = null },
            channel.Id,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        var (_, body) = Assert.Single(_webhooks.Received);
        Assert.Contains("notifications to renamed work", body, StringComparison.Ordinal);
        Assert.Equal("#ops-alerts", (await new ChannelStore(database).GetAsync(channel.Id, default))!.Name);
    }

    [Fact]
    public async Task TestDraftAsync_NewWebhookWithoutUrl_IsRefused()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);

        await Assert.ThrowsAsync<AlertingValidationException>(
            () => Service(database).TestDraftAsync(Webhook() with { WebhookUrl = null }, null, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_ChannelThatNeverSent_HasFourteenEmptyDays()
    {
        await using var database = await AlertingDatabase.CreateAsync(postgres);
        var service = Service(database);
        await service.CreateAsync(Webhook(), CancellationToken.None);

        var (_, activity) = Assert.Single(await service.ListAsync(CancellationToken.None));

        Assert.Equal(14, activity.Days.Count);
        Assert.Equal(new DateOnly(2026, 11, 10), activity.Days[^1].Day);
        Assert.Null(activity.LastAttemptAt);
    }

    public void Dispose() => _webhooks.Dispose();

    private static ChannelInput Webhook() => new(
        Name: "#ops-alerts",
        Kind: ChannelKind.Webhook,
        Enabled: true,
        SendWarning: true,
        SendCaution: true,
        SendResolved: true,
        WebhookUrl: _url,
        EmailRecipients: null);

    private static ChannelInput Email(params string[] recipients) => new(
        Name: "On-call",
        Kind: ChannelKind.Email,
        Enabled: true,
        SendWarning: true,
        SendCaution: false,
        SendResolved: true,
        WebhookUrl: null,
        EmailRecipients: recipients);

    private ChannelService Service(NpgsqlDataSource database)
    {
        var options = Options.Create(new AlertingOptions());
        var protector = new WebhookUrlProtector(_keys);
        var sender = new ChannelSender(new WebhookSender(_webhooks, options), new EmailSender(options), protector);

        return new ChannelService(new ChannelStore(database), new DeliveryStore(database), protector, sender, _clock);
    }
}
