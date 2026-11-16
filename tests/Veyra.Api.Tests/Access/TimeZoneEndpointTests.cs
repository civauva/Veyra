using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Veyra.Testing;

namespace Veyra.Api.Tests.Access;

/// <summary>A time zone per person, kept on the account (VEY-22).</summary>
[Collection(PostgresGroup.Name)]
public class TimeZoneEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PutTimeZone_KnownZone_IsKeptOnTheAccountAndTheSession()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var put = await admin.PutAsJsonAsync("/api/v1/account/time-zone", new { timeZone = "Europe/Berlin" });
        var account = await admin.GetFromJsonAsync<JsonElement>("/api/v1/account");
        var session = await admin.GetFromJsonAsync<JsonElement>("/api/v1/session");

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal("Europe/Berlin", account.GetProperty("timeZone").GetString());
        Assert.Equal("Europe/Berlin", session.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task PutTimeZone_UnknownZone_IsRefusedAndKeepsTheOldOne()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var first = await admin.PutAsJsonAsync("/api/v1/account/time-zone", new { timeZone = "Asia/Tokyo" });

        using var wrong = await admin.PutAsJsonAsync("/api/v1/account/time-zone", new { timeZone = "Mars/Olympus_Mons" });
        var account = await admin.GetFromJsonAsync<JsonElement>("/api/v1/account");

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("Asia/Tokyo", account.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task PutTimeZone_Null_GoesBackToTheBrowsersZone()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        using var first = await admin.PutAsJsonAsync("/api/v1/account/time-zone", new { timeZone = "Asia/Tokyo" });

        using var cleared = await admin.PutAsJsonAsync("/api/v1/account/time-zone", new { timeZone = (string?)null });
        var account = await admin.GetFromJsonAsync<JsonElement>("/api/v1/account");

        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Equal(JsonValueKind.Null, account.GetProperty("timeZone").ValueKind);
    }

    [Fact]
    public async Task AlertSummary_TimeZone_StartsTheDaysAtLocalMidnight()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati");

        var summary = await admin.GetFromJsonAsync<JsonElement>(
            "/api/v1/alerts/summary?days=7&timeZone=Pacific%2FKiritimati");
        using var wrong = await admin.GetAsync(
            new Uri("/api/v1/alerts/summary?days=7&timeZone=Nowhere%2FElse", UriKind.Relative));

        var from = summary.GetProperty("from").GetDateTimeOffset();
        Assert.Equal(TimeSpan.Zero, TimeZoneInfo.ConvertTime(from, zone).TimeOfDay);
        Assert.Equal(7, summary.GetProperty("days").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
    }
}
