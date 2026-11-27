using System.Buffers.Text;
using System.Text.Json;
using Veyra.Core.Alerts;
using Veyra.Core.Jobs;
using Veyra.Core.Telemetry;

namespace Veyra.Query.Jobs;

/// <summary>
/// A job's ID in the API (ADR 0027): its source, environment, block, and name as a JSON array, in URL-safe base64. It
/// is opaque to the screens, the same every time, and needs no table of its own, because the job is its identity.
/// </summary>
internal static class JobIds
{
    /// <summary>The ID of a job.</summary>
    /// <param name="job">The job.</param>
    /// <returns>The ID, which holds only letters, digits, <c>-</c>, and <c>_</c>.</returns>
    public static string Encode(JobKey job)
    {
        ArgumentNullException.ThrowIfNull(job);

        string?[] parts = [JobText.Source(job.Source), job.Environment, job.Block.Name, job.Name];

        return Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(parts));
    }

    /// <summary>Reads a job's ID.</summary>
    /// <param name="id">The ID, as the API gave it.</param>
    /// <param name="job">The job, when the ID is one.</param>
    /// <returns>Whether the ID names a job; anything else, such as a guessed or cut ID, does not.</returns>
    public static bool TryDecode(string? id, out JobKey job)
    {
        job = null!;

        if (string.IsNullOrEmpty(id) || !Base64Url.IsValid(id))
        {
            return false;
        }

        string?[]? parts;

        try
        {
            parts = JsonSerializer.Deserialize<string?[]>(Base64Url.DecodeFromChars(id));
        }
        catch (JsonException)
        {
            return false;
        }

        if (parts is not [{ } source, var environment, { } block, { } name]
            || JobText.SourceOf(source) is not { } known)
        {
            return false;
        }

        // A traced job runs in a service, a SQL Server Agent job on a database server.
        var kind = known == JobSource.SqlAgent ? AlertBlockKind.Database : AlertBlockKind.Service;
        job = new JobKey(known, environment, new TelemetryBlock(kind, block), name);

        return true;
    }
}
