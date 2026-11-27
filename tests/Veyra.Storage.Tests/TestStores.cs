using Npgsql;
using Veyra.Storage.Traces;
using Veyra.Testing;

namespace Veyra.Storage.Tests;

/// <summary>The stores as the tests build them, with the Storage settings a fresh installation has.</summary>
internal static class TestStores
{
    /// <summary>A span store with the default settings, among them the attributes that name a job.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The store.</returns>
    public static SpanStore Spans(NpgsqlDataSource database) =>
        new(database, new TestOptionsMonitor<StorageOptions>(new StorageOptions()));
}
