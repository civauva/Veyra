namespace Veyra.Agents.SqlServers;

/// <summary>How the agent's connection to a SQL Server is encrypted. Stored as its number.</summary>
public enum SqlServerEncryption
{
    /// <summary>Encrypted, and the server's certificate must be trusted by the agent's machine.</summary>
    Required = 1,

    /// <summary>
    /// Encrypted, accepting the server's own certificate. SQL Server makes a self-signed one when none is installed.
    /// </summary>
    TrustServerCertificate = 2,

    /// <summary>Only the login is encrypted, as SQL Server does for clients that ask for nothing more.</summary>
    Off = 3,
}
