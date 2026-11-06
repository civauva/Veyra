namespace Veyra.Core;

/// <summary>
/// Configuration section names. Every module binds its options from <c>Veyra:&lt;Module&gt;</c>.
/// </summary>
public static class VeyraOptionNames
{
    public const string Root = "Veyra";
    public const string Ingestion = Root + ":Ingestion";
    public const string Storage = Root + ":Storage";
    public const string Query = Root + ":Query";
    public const string Alerting = Root + ":Alerting";
}
