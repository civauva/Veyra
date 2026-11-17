namespace Veyra.Agents.PostgresServers;

/// <summary>How the agent's connection to a PostgreSQL server is encrypted. Stored as its number.</summary>
public enum PostgresEncryption
{
    /// <summary>
    /// TLS, and the server's certificate and name must be trusted by the agent's machine (<c>sslmode=verify-full</c>).
    /// </summary>
    Required = 1,

    /// <summary>
    /// TLS, accepting whatever certificate the server shows (<c>sslmode=require</c>), such as a self-signed one.
    /// </summary>
    TrustServerCertificate = 2,

    /// <summary>
    /// No TLS at all (<c>sslmode=disable</c>): the password and every figure cross the network readable.
    /// </summary>
    Off = 3,
}
