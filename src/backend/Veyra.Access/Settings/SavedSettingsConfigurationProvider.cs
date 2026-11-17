using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Veyra.Core;

namespace Veyra.Access.Settings;

/// <summary>
/// Reads the saved settings from <c>access.setting</c> into configuration. It loads once when configuration is built,
/// again when the Access module starts, and whenever a save changes the version (ADR 0017).
/// </summary>
/// <remarks>
/// <para>
/// Configuration is built before the database is migrated and before Data Protection exists. So the first load
/// tolerates a missing table or an unreachable database by keeping what it has, and secrets are read only once
/// <see cref="Activate"/> has given it a protector.
/// </para>
/// <para>Load runs synchronously, as configuration providers do. The query reads a few dozen rows.</para>
/// </remarks>
/// <param name="connectionString">The PostgreSQL connection string.</param>
internal sealed class SavedSettingsConfigurationProvider(string connectionString) : ConfigurationProvider
{
    // PostgreSQL's codes for a missing table and a missing column: the schema is not migrated yet.
    private static readonly string[] _notMigrated = ["42P01", "42703"];

    private IDataProtector? _protector;

    /// <summary>The number of the last change this provider has read; 0 before the first.</summary>
    public long Version { get; private set; }

    /// <summary>Gives the provider the protector its secrets are encrypted with, then reads them.</summary>
    /// <param name="protection">The application's Data Protection.</param>
    public void Activate(IDataProtectionProvider protection)
    {
        ArgumentNullException.ThrowIfNull(protection);

        _protector = protection.CreateProtector(SettingStore.SecretPurpose);
        Refresh();
    }

    /// <summary>Reads the saved settings again and tells everyone bound to configuration.</summary>
    public void Refresh()
    {
        Load();
        OnReload();
    }

    /// <inheritdoc />
    public override void Load()
    {
        try
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();

            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            using (var command = new NpgsqlCommand(
                "SELECT name, value, secret FROM access.setting WHERE name LIKE $1",
                connection))
            {
                command.Parameters.AddWithValue(VeyraOptionNames.Root + ":%");
                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    var value = reader.GetBoolean(2) ? Unprotect(reader.GetString(1)) : reader.GetString(1);

                    if (value is not null)
                    {
                        data[reader.GetString(0)] = value;
                    }
                }
            }

            using (var version = new NpgsqlCommand(SettingStore.VersionSql, connection))
            {
                Version = (long)version.ExecuteScalar()!;
            }

            Data = data;
        }
        catch (PostgresException exception) when (_notMigrated.Contains(exception.SqlState))
        {
            // First start: the Access migration that creates the columns has not run yet. Startup loads again after.
        }
        catch (Exception exception) when (exception is NpgsqlException or SocketException or TimeoutException)
        {
            // The database is not reachable now. Keep the values already read; the next refresh tries again.
        }
    }

    private string? Unprotect(string value)
    {
        if (_protector is null)
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            // The key ring was lost or replaced; the secret has to be entered again.
            return null;
        }
    }
}
