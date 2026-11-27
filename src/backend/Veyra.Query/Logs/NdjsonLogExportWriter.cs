using System.Globalization;
using System.Text.Json;

namespace Veyra.Query.Logs;

/// <summary>
/// Writes a log export as newline-delimited JSON: each record on its own line, shaped as <c>GET /api/v1/logs</c>
/// shapes it, so a script reads an export as it reads a page.
/// </summary>
/// <param name="writer">Where the file is written.</param>
/// <param name="json">The API's JSON settings.</param>
internal sealed class NdjsonLogExportWriter(TextWriter writer, JsonSerializerOptions json) : LogExportWriter(writer)
{
    /// <inheritdoc />
    public override string Extension => "ndjson";

    /// <inheritdoc />
    public override string ContentType => "application/x-ndjson";

    /// <inheritdoc />
    public override Task WriteAsync(LogEntryResponse entry, CancellationToken cancellationToken) =>
        WriteTextAsync(JsonSerializer.Serialize(entry, json) + "\n", cancellationToken);

    /// <inheritdoc />
    /// <remarks>A record never has a <c>truncated</c> member, so the line cannot be mistaken for one.</remarks>
    public override Task WriteTruncatedAsync(int limit, CancellationToken cancellationToken) =>
        WriteTextAsync(
            string.Create(CultureInfo.InvariantCulture, $"{{\"truncated\":true,\"limit\":{limit}}}\n"),
            cancellationToken);
}
