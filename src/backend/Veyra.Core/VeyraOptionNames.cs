namespace Veyra.Core;

/// <summary>
/// Configuration section names. Every module binds its options from <c>Veyra:&lt;Module&gt;</c>.
/// </summary>
/// <remarks>
/// In environment variables the colon becomes a double underscore, for example
/// <c>Veyra__Storage__ConnectionString</c>.
/// </remarks>
public static class VeyraOptionNames
{
    /// <summary>The root section that holds every Veyra setting.</summary>
    public const string Root = "Veyra";

    /// <summary>Settings of the Ingestion module: OTLP receivers and buffering.</summary>
    public const string Ingestion = Root + ":Ingestion";

    /// <summary>Settings of the Storage module: the PostgreSQL connection, migrations, and retention.</summary>
    public const string Storage = Root + ":Storage";

    /// <summary>Settings of the Query module: search and paging limits.</summary>
    public const string Query = Root + ":Query";

    /// <summary>Settings of the Alerting module: rule evaluation and notifications.</summary>
    public const string Alerting = Root + ":Alerting";

    /// <summary>Settings of the Access module: sessions, passwords, lockout, and ingestion keys.</summary>
    public const string Access = Root + ":Access";
}
