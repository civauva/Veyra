using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using Veyra.Agents.Integrations;

namespace Veyra.Agents.SqlServers;

/// <summary>Reads and writes the SQL Servers agents read, in <c>agents.sql_server</c>.</summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class SqlServerStore(NpgsqlDataSource dataSource)
{
    private const string _columns =
        "id, name, address, port, environment, agent_id, login, encryption, created_at, changed_at, password, " +
        "platform, database_name, instance, authentication, modules, template_version, template_set_at, " +
        "template_set_by";

    // PostgreSQL's code for a unique violation, raised when another server has the name.
    private const string _uniqueViolation = "23505";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>Stores a new server.</summary>
    /// <param name="server">The server.</param>
    /// <param name="protectedPassword">Its password, already encrypted.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the server is stored.</returns>
    /// <exception cref="AgentsConflictException">Another server has the name.</exception>
    public async Task InsertAsync(SqlServer server, string protectedPassword, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
            INSERT INTO agents.sql_server ({_columns})
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19)
            """);
        AddAll(command, server, protectedPassword);

        await ExecuteAsync(command, server.Name, cancellationToken);
    }

    /// <summary>Replaces a stored server.</summary>
    /// <param name="server">The server as it should be. Its ID and creation time stay.</param>
    /// <param name="protectedPassword">
    /// Its encrypted password, or <see langword="null"/> to keep the stored one.
    /// </param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>Whether a server had the ID.</returns>
    /// <exception cref="AgentsConflictException">Another server has the name.</exception>
    public async Task<bool> UpdateAsync(
        SqlServer server,
        string? protectedPassword,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agents.sql_server
            SET name = $2, address = $3, port = $4, environment = $5, agent_id = $6, login = $7, encryption = $8,
                changed_at = $10, password = COALESCE($11, password), platform = $12,
                database_name = $13, instance = $14, authentication = $15, modules = $16, template_version = $17,
                template_set_at = $18, template_set_by = $19
            WHERE id = $1
            """);
        AddAll(command, server, protectedPassword);

        return await ExecuteAsync(command, server.Name, cancellationToken) == 1;
    }

    /// <summary>Removes a server. The data it sent stays until retention removes it.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>Whether a server had the ID.</returns>
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("DELETE FROM agents.sql_server WHERE id = $1");
        command.Parameters.AddWithValue(id);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Finds a server with its encrypted password.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The server, or <see langword="null"/> when no server has the ID.</returns>
    public async Task<(SqlServer Server, string ProtectedPassword)?> FindAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {_columns} FROM agents.sql_server WHERE id = $1");
        command.Parameters.AddWithValue(id);

        var found = await ReadAllAsync(command, cancellationToken);

        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>Lists every server by name, with its encrypted password.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The servers.</returns>
    public async Task<IReadOnlyList<(SqlServer Server, string ProtectedPassword)>> ListAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($"SELECT {_columns} FROM agents.sql_server ORDER BY name");

        return await ReadAllAsync(command, cancellationToken);
    }

    /// <summary>Lists the servers one agent reads, by name, with their encrypted passwords.</summary>
    /// <param name="agentId">The agent's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The servers.</returns>
    public async Task<IReadOnlyList<(SqlServer Server, string ProtectedPassword)>> ListForAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM agents.sql_server WHERE agent_id = $1 ORDER BY name");
        command.Parameters.AddWithValue(agentId);

        return await ReadAllAsync(command, cancellationToken);
    }

    private static void AddAll(NpgsqlCommand command, SqlServer server, string? protectedPassword)
    {
        command.Parameters.AddWithValue(server.Id);
        command.Parameters.AddWithValue(server.Name);
        command.Parameters.AddWithValue(server.Address);
        command.Parameters.AddWithValue(server.Port);
        command.Parameters.AddWithValue(server.Environment);
        command.Parameters.AddWithValue(server.AgentId);
        command.Parameters.AddWithValue(server.Login);
        command.Parameters.AddWithValue((short)server.Encryption);
        command.Parameters.AddWithValue(server.CreatedAt);
        command.Parameters.AddWithValue(server.ChangedAt);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)protectedPassword ?? DBNull.Value);
        command.Parameters.AddWithValue((short)server.Platform);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)server.Database ?? DBNull.Value);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)server.Instance ?? DBNull.Value);
        command.Parameters.AddWithValue((short)server.Authentication);

        // Only what differs from the defaults is stored, so a later default reaches every server that kept it.
        command.Parameters.AddWithValue(
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(SqlServerModules.Overrides(server.Choices), _json));
        command.Parameters.AddWithValue(server.Version);
        command.Parameters.AddWithValue(server.TemplateSetAt ?? server.CreatedAt);
        command.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)server.TemplateSetBy ?? DBNull.Value);
    }

    private static async Task<int> ExecuteAsync(NpgsqlCommand command, string name, CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == _uniqueViolation)
        {
            throw new AgentsConflictException($"Another SQL Server is already called {name}.", exception);
        }
    }

    private static async Task<IReadOnlyList<(SqlServer, string)>> ReadAllAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var servers = new List<(SqlServer, string)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var server = new SqlServer(
                Id: reader.GetGuid(0),
                Name: reader.GetString(1),
                Address: reader.GetString(2),
                Port: reader.GetInt32(3),
                Environment: reader.GetString(4),
                AgentId: reader.GetGuid(5),
                Login: reader.GetString(6),
                Encryption: (SqlServerEncryption)reader.GetInt16(7),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(8),
                ChangedAt: reader.GetFieldValue<DateTimeOffset>(9),
                Platform: (SqlServerPlatform)reader.GetInt16(11),
                Database: reader.IsDBNull(12) ? null : reader.GetString(12),
                Instance: reader.IsDBNull(13) ? null : reader.GetString(13),
                Authentication: (SqlServerAuthentication)reader.GetInt16(14),
                Modules: SqlServerModules.FromStored(Modules(reader.GetString(15))),
                TemplateVersion: reader.GetString(16),
                TemplateSetAt: reader.GetFieldValue<DateTimeOffset>(17),
                TemplateSetBy: reader.IsDBNull(18) ? null : reader.GetString(18));

            servers.Add((server, reader.GetString(10)));
        }

        return servers;
    }

    // Stored choices that cannot be read count as none: the server reads at its defaults rather than not at all.
    private static List<SqlServerModuleChoice> Modules(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<SqlServerModuleChoice>>(json, _json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
