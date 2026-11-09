namespace Veyra.Testing;

/// <summary>
/// Groups the tests that share one PostgreSQL container. Each test project that uses it declares its own
/// collection with this name, because xUnit only discovers collection definitions in the test assembly itself.
/// </summary>
public static class PostgresGroup
{
    /// <summary>The collection name to use in <c>[Collection(PostgresGroup.Name)]</c>.</summary>
    public const string Name = "PostgreSQL";
}
