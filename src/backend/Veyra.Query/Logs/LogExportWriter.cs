using System.Text.Json;

namespace Veyra.Query.Logs;

/// <summary>
/// Writes the records of a log export to the response, one record at a time, so an export of any size needs the
/// memory of one page (ADR 0024).
/// </summary>
/// <param name="writer">Where the file is written.</param>
internal abstract class LogExportWriter(TextWriter writer)
{
    /// <summary>The file name's extension, without the dot.</summary>
    public abstract string Extension { get; }

    /// <summary>The media type of the response.</summary>
    public abstract string ContentType { get; }

    /// <summary>Picks the writer of a format.</summary>
    /// <param name="format"><c>ndjson</c>, the default, or <c>csv</c>, in any case.</param>
    /// <param name="writer">Where the file is written.</param>
    /// <param name="json">The API's JSON settings, so a record has the shape <c>GET /api/v1/logs</c> gives it.</param>
    /// <returns>The writer, or <see langword="null"/> for a format Veyra does not write.</returns>
    public static LogExportWriter? For(string? format, TextWriter writer, JsonSerializerOptions json) =>
        format?.ToLowerInvariant() switch
        {
            null or "" or "ndjson" => new NdjsonLogExportWriter(writer, json),
            "csv" => new CsvLogExportWriter(writer, json),
            _ => null,
        };

    /// <summary>Writes what comes before the first record, if the format has anything.</summary>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public virtual Task WriteHeaderAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Writes one record.</summary>
    /// <param name="entry">The record, as the logs API shapes it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public abstract Task WriteAsync(LogEntryResponse entry, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the last line of an export that stopped at the limit while more records matched, so nobody takes a cut
    /// file for the whole result.
    /// </summary>
    /// <param name="limit">The limit it stopped at.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    public abstract Task WriteTruncatedAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Writes text, honouring the cancellation.</summary>
    /// <param name="text">The text.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when it is written.</returns>
    protected Task WriteTextAsync(string text, CancellationToken cancellationToken) =>
        writer.WriteAsync(text.AsMemory(), cancellationToken);
}
