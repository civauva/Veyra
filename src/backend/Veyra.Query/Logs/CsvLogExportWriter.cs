using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Veyra.Query.Logs;

/// <summary>
/// Writes a log export as comma-separated values (RFC 4180), for a spreadsheet: one header row, then one row per
/// record, with the attributes as JSON text.
/// </summary>
/// <remarks>
/// A log message is written by whoever sends telemetry. A value that starts like a formula (<c>=</c>, <c>+</c>,
/// <c>-</c>, <c>@</c>, a tab, or a carriage return) gets a leading <c>'</c>, so a spreadsheet shows it as text instead
/// of running it.
/// </remarks>
/// <param name="writer">Where the file is written.</param>
/// <param name="json">The API's JSON settings, for the attributes.</param>
internal sealed class CsvLogExportWriter(TextWriter writer, JsonSerializerOptions json) : LogExportWriter(writer)
{
    private const string _header =
        "timestamp,observedTimestamp,severity,severityText,serviceName,environment,hostName,traceId,spanId,body,"
        + "attributes,resourceAttributes\r\n";

    /// <inheritdoc />
    public override string Extension => "csv";

    /// <inheritdoc />
    public override string ContentType => "text/csv; charset=utf-8";

    /// <inheritdoc />
    public override Task WriteHeaderAsync(CancellationToken cancellationToken) =>
        WriteTextAsync(_header, cancellationToken);

    /// <inheritdoc />
    public override Task WriteAsync(LogEntryResponse entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        string?[] values =
        [
            entry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            entry.ObservedTimestamp.ToString("O", CultureInfo.InvariantCulture),
            entry.Severity.ToString(),
            entry.SeverityText,
            entry.ServiceName,
            entry.Environment,
            entry.HostName,
            entry.TraceId,
            entry.SpanId,
            entry.Body,
            JsonSerializer.Serialize(entry.Attributes, json),
            JsonSerializer.Serialize(entry.ResourceAttributes, json),
        ];

        var row = new StringBuilder();

        for (var index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                row.Append(',');
            }

            row.Append(Field(values[index]));
        }

        row.Append("\r\n");

        return WriteTextAsync(row.ToString(), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>A comment line, which spreadsheets show as a row and CSV readers that know comments skip.</remarks>
    public override Task WriteTruncatedAsync(int limit, CancellationToken cancellationToken) =>
        WriteTextAsync(
            string.Create(CultureInfo.InvariantCulture, $"# truncated at {limit} records\r\n"),
            cancellationToken);

    private static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
