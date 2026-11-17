namespace Veyra.Core.Settings;

/// <summary>What kind of monitored system or agent a change in the settings history is about (ADR 0019).</summary>
public enum MonitoringSubject
{
    /// <summary>A SQL Server, Managed Instance, or Azure SQL Database an agent reads.</summary>
    SqlServer = 1,

    /// <summary>A PostgreSQL server an agent reads.</summary>
    PostgresServer = 2,

    /// <summary>An agent: Veyra's built-in one or one installed in another network.</summary>
    Agent = 3,
}
