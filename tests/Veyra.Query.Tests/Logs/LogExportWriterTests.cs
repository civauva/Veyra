using System.Text.Json;
using System.Text.Json.Serialization;
using Veyra.Core.Telemetry;
using Veyra.Query.Logs;

namespace Veyra.Query.Tests.Logs;

/// <summary>The files a log export writes (ADR 0024): one JSON object a line, or comma-separated values.</summary>
public class LogExportWriterTests
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly DateTimeOffset _at = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, "ndjson", "application/x-ndjson")]
    [InlineData("NDJSON", "ndjson", "application/x-ndjson")]
    [InlineData("csv", "csv", "text/csv; charset=utf-8")]
    public void For_KnownFormat_HasItsExtensionAndType(string? format, string extension, string type)
    {
        var writer = LogExportWriter.For(format, TextWriter.Null, _json);

        Assert.Equal((extension, type), (writer!.Extension, writer.ContentType));
    }

    [Fact]
    public void For_UnknownFormat_IsNull()
    {
        Assert.Null(LogExportWriter.For("xml", TextWriter.Null, _json));
    }

    [Fact]
    public async Task Ndjson_Records_AreOneObjectALineAndEndWithTheCut()
    {
        await using var text = new StringWriter();
        var writer = LogExportWriter.For("ndjson", text, _json)!;

        await writer.WriteHeaderAsync(default);
        await writer.WriteAsync(Entry("payment declined"), default);
        await writer.WriteAsync(Entry("payment accepted"), default);
        await writer.WriteTruncatedAsync(2, default);

        var lines = text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        Assert.Equal("payment declined", first.RootElement.GetProperty("body").GetString());
        Assert.Equal("Error", first.RootElement.GetProperty("severity").GetString());
        Assert.Equal("web-1", first.RootElement.GetProperty("hostName").GetString());
        Assert.Equal("{\"truncated\":true,\"limit\":2}", lines[2]);
    }

    [Fact]
    public async Task Csv_Records_HaveAHeaderQuoteWhatNeedsItAndEndWithTheCut()
    {
        await using var text = new StringWriter();
        var writer = LogExportWriter.For("csv", text, _json)!;

        await writer.WriteHeaderAsync(default);
        await writer.WriteAsync(Entry("said \"no\", then\nleft") with { Environment = null }, default);
        await writer.WriteTruncatedAsync(1, default);

        var lines = text.ToString().Split("\r\n");
        Assert.Equal(
            "timestamp,observedTimestamp,severity,severityText,serviceName,environment,hostName,traceId,spanId,body,"
            + "attributes,resourceAttributes",
            lines[0]);
        Assert.Equal(
            "2026-11-26T12:00:00.0000000+00:00,2026-11-26T12:00:00.0000000+00:00,Error,ERROR,payments,,web-1,,,"
            + "\"said \"\"no\"\", then\nleft\",\"{\"\"http.route\"\":\"\"/pay\"\"}\",{}",
            lines[1]);
        Assert.Equal("# truncated at 1 records", lines[2]);
    }

    [Theory]
    [InlineData("=HYPERLINK(A1)")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("@SUM(A1)")]
    public async Task Csv_ValueAFormulaWouldStartWith_IsWrittenAsText(string body)
    {
        await using var text = new StringWriter();
        var writer = LogExportWriter.For("csv", text, _json)!;

        await writer.WriteAsync(Entry(body), default);

        Assert.Contains(",'" + body[0], text.ToString(), StringComparison.Ordinal);
    }

    private static LogEntryResponse Entry(string body) => new(
        1,
        _at,
        _at,
        LogSeverity.Error,
        "ERROR",
        body,
        "payments",
        "production",
        "web-1",
        null,
        null,
        new Dictionary<string, string> { ["http.route"] = "/pay" },
        new Dictionary<string, string>());
}
