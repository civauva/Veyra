using Npgsql;

namespace Veyra.Agents.PostgresServers;

/// <summary>Reads and writes the PostgreSQL servers agents read, in <c>agents.postgres_server</c>.</summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class PostgresServerStore(NpgsqlDataSource dataSource)
{
    private const string _columns =
        "id, name, address, port, environment, agent_id, login, encryption, created_at, changed_at, password";

    // PostgreSQL's code for a unique violation, raised when another server has the name.
    private const string _uniqueViolation = "23505";

    /// <summary>Stores a new server.</summary>
    /// <param name="server">The server.</param>
    /// <param name="protectedPassword">Its password, already encrypted.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the server is stored.</returns>
    /// <exception cref="AgentsConflictException">Another server has the name.</exception>
    public async Task InsertAsync(PostgresServer server, string protectedPassword, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"INSERT INTO agents.postgres_server ({_columns}) VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)");
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
        PostgresServer server,
        string? protectedPassword,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE agents.postgres_server
            SET name = $2, address = $3, port = $4, environment = $5, agent_id = $6, login = $7, encryption = $8,
                changed_at = $10, password = COALESCE($11, password)
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
        await using var command = dataSource.CreateCommand("DELETE FROM agents.postgres_server WHERE id = $1");
        command.Parameters.AddWithValue(id);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Finds a server with its encrypted password.</summary>
    /// <param name="id">The server's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The server, or <see langword="null"/> when no server has the ID.</returns>
    public async Task<(PostgresServer Server, string ProtectedPassword)?> FindAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM agents.postgres_server WHERE id = $1");
        command.Parameters.AddWithValue(id);

        var found = await ReadAllAsync(command, cancellationToken);

        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>Lists every server by name, with its encrypted password.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The servers.</returns>
    public async Task<IReadOnlyList<(PostgresServer Server, string ProtectedPassword)>> ListAsync(
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM agents.postgres_server ORDER BY name");

        return await ReadAllAsync(command, cancellationToken);
    }

    /// <summary>Lists the servers one agent reads, by name, with their encrypted passwords.</summary>
    /// <param name="agentId">The agent's ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The servers.</returns>
    public async Task<IReadOnlyList<(PostgresServer Server, string ProtectedPassword)>> ListForAgentAsync(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"SELECT {_columns} FROM agents.postgres_server WHERE agent_id = $1 ORDER BY name");
        command.Parameters.AddWithValue(agentId);

        return await ReadAllAsync(command, cancellationToken);
    }

    private static void AddAll(NpgsqlCommand command, PostgresServer server, string? protectedPassword)
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
        command.Parameters.AddWithValue(NpgsqlTypes.NpgsqlDbType.Text, (object?)protectedPassword ?? DBNull.Value);
    }

    private static async Task<int> ExecuteAsync(NpgsqlCommand command, string name, CancellationToken cancellationToken)
    {
        try
        {
            return await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == _uniqueViolation)
        {
            throw new AgentsConflictException($"Another PostgreSQL server is already called {name}.", exception);
        }
    }

    private static async Task<IReadOnlyList<(PostgresServer, string)>> ReadAllAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var servers = new List<(PostgresServer, string)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var server = new PostgresServer(
                Id: reader.GetGuid(0),
                Name: reader.GetString(1),
                Address: reader.GetString(2),
                Port: reader.GetInt32(3),
                Environment: reader.GetString(4),
                AgentId: reader.GetGuid(5),
                Login: reader.GetString(6),
                Encryption: (PostgresEncryption)reader.GetInt16(7),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(8),
                ChangedAt: reader.GetFieldValue<DateTimeOffset>(9));

            servers.Add((server, reader.GetString(10)));
        }

        return servers;
    }
}
