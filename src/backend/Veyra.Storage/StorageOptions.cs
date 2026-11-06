namespace Veyra.Storage;

public sealed class StorageOptions
{
    /// <summary>
    /// PostgreSQL connection string. Supply it through configuration or the
    /// <c>Veyra__Storage__ConnectionString</c> environment variable; never commit it.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}
