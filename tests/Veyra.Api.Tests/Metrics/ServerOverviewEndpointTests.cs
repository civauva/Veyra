using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Veyra.Core.Activity;
using Veyra.Testing;

namespace Veyra.Api.Tests.Metrics;

/// <summary>
/// A SQL Server's Overview and Backups tabs (ADR 0025) answer through the whole API with the names the Console reads,
/// refuse a window they cannot read, and say 404 for a server that sent nothing.
/// </summary>
[Collection(PostgresGroup.Name)]
public class ServerOverviewEndpointTests(PostgresFixture postgres)
{
    private const string _server = "/api/v1/metrics/databases/microsoft.sql_server/sql-prod-01/";

    private const string _block = "microsoft.sql_server sql-prod-01";

    [Theory]
    [InlineData("overview?range=1h&system=true", HttpStatusCode.NotFound)]
    [InlineData("backups?range=7d", HttpStatusCode.NotFound)]
    [InlineData("overview?from=2026-11-20T00:00:00Z&to=2026-11-10T00:00:00Z", HttpStatusCode.BadRequest)]
    [InlineData("backups?from=2026-09-01T00:00:00Z&to=2026-11-10T00:00:00Z", HttpStatusCode.BadRequest)]
    public async Task Get_ServerThatSentNothingOrABadWindow_IsRefused(string path, HttpStatusCode expected)
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();

        using var response = await admin.GetAsync(new Uri(_server + path, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Get_AfterTheAgentSent_AnswersEveryPartUnderItsName()
    {
        await using var factory = await VeyraApiFactory.CreateAsync(postgres);
        using var admin = await factory.CreateAdminClientAsync();
        var now = DateTimeOffset.UtcNow;
        await factory.Services.GetRequiredService<IDatabaseActivityWriter>().WriteAsync(
            [
                new ServerSample(_block, "production", now.AddSeconds(-5), 65_536, 61_440, 0),
                Backup("41", "shop", "full", now.AddHours(-3), 1_800),
                Backup("42", "shop", "log", now.AddMinutes(-10), 4),
            ],
            CancellationToken.None);

        using var backups = await GetAsync(admin, "backups?range=24h");
        using var overview = await GetAsync(admin, "overview?range=1h");

        var root = backups.RootElement;
        Assert.Equal(
            ["from", "to", "selfManaged", "backups", "days", "grid", "freshness"],
            root.EnumerateObject().Select(property => property.Name));
        var newest = root.GetProperty("backups")[0];
        Assert.Equal(
            [
                "database", "type", "startedAt", "finishedAt", "seconds", "bytes", "compressedBytes", "copyOnly",
                "device",
            ],
            newest.EnumerateObject().Select(property => property.Name));
        Assert.Equal(("shop", "log", 4d), (
            newest.GetProperty("database").GetString(),
            newest.GetProperty("type").GetString(),
            Math.Round(newest.GetProperty("seconds").GetDouble())));
        Assert.Equal(7, root.GetProperty("grid").GetProperty("days").GetArrayLength());
        Assert.Equal(
            "shop",
            root.GetProperty("grid").GetProperty("databases")[0].GetProperty("database").GetString());
        Assert.Equal(
            ["day", "full", "differential", "log"],
            root.GetProperty("days")[0].EnumerateObject().Select(property => property.Name));

        var view = overview.RootElement;
        Assert.Equal(
            [
                "from", "to", "machine", "figures", "performance", "counters", "io", "waits", "storage", "inventory",
                "blocking", "coverage",
            ],
            view.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            [
                "cpu", "sqlMemory", "sessions", "blocked", "size", "lastFullBackupSeconds", "batchRequests",
                "volumeFree",
            ],
            view.GetProperty("figures").EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            [
                "totalBytes", "growth24hBytes", "growth7dBytes", "growth30dBytes", "line", "largest", "volumes",
            ],
            view.GetProperty("storage").EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            [
                "version", "updateLevel", "edition", "osName", "osRelease", "cpus", "memoryBytes", "startedAt",
                "uptimeSeconds", "databases", "platform",
            ],
            view.GetProperty("machine").EnumerateObject().Select(property => property.Name));
        var activity = view.GetProperty("coverage")[0];
        Assert.Equal(("activity", true), (
            activity.GetProperty("area").GetString(),
            activity.GetProperty("delivering").GetBoolean()));
        Assert.Equal(9, view.GetProperty("coverage").GetArrayLength());
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(_server + path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static ActivityRecord Backup(
        string key,
        string database,
        string type,
        DateTimeOffset finished,
        int seconds) =>
        new(
            _block,
            "production",
            finished,
            "sqlserver.backup",
            key,
            new Dictionary<string, string>
            {
                ["database_name"] = database,
                ["backup_type"] = type,
                ["started"] = finished.AddSeconds(-seconds)
                    .ToUnixTimeMilliseconds()
                    .ToString(CultureInfo.InvariantCulture),
                ["bytes"] = "1048576",
                ["compressed_bytes"] = "524288",
                ["copy_only"] = "0",
            },
            "/backup/" + key);
}
