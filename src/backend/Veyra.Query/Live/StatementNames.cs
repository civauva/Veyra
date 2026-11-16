using System.Text.RegularExpressions;

namespace Veyra.Query.Live;

/// <summary>
/// Names a SQL statement by what it does, for the Simple view (ADR 0020): "Update of Orders by CustomerId" instead of
/// the statement itself, which stays one click away.
/// </summary>
/// <remarks>
/// A few patterns read the first statement's verb, its table without schema or brackets, and the first column its
/// <c>WHERE</c> compares. Anything they cannot read keeps a short form of the text, so a name is never wrong, only
/// plainer.
/// </remarks>
internal static partial class StatementNames
{
    private const int _plainLength = 60;

    /// <summary>Names a statement.</summary>
    /// <param name="text">The statement or batch, as SQL Server reports it.</param>
    /// <returns>Its name, or a short form of its text.</returns>
    public static string Describe(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var sql = Comments().Replace(text, " ");
        sql = Space().Replace(sql, " ").Trim();

        if (sql.Length == 0)
        {
            return "Nothing sent yet";
        }

        var transaction = BeginTransaction().Match(sql);

        if (transaction.Success)
        {
            var rest = sql[transaction.Length..].TrimStart(';', ' ');

            return rest.Length == 0 ? "Transaction" : $"Transaction: {Lower(Describe(rest))}";
        }

        if (Procedure().Match(sql) is { Success: true } procedure)
        {
            return $"Run {Name(procedure.Groups["name"].Value)}";
        }

        var filter = Filter(sql);

        if (Update().Match(sql) is { Success: true } update)
        {
            return $"Update of {Name(update.Groups["table"].Value)}{filter}";
        }

        if (Delete().Match(sql) is { Success: true } delete)
        {
            return $"Delete from {Name(delete.Groups["table"].Value)}{filter}";
        }

        if (Insert().Match(sql) is { Success: true } insert)
        {
            return $"Insert into {Name(insert.Groups["table"].Value)}";
        }

        if (Merge().Match(sql) is { Success: true } merge)
        {
            return $"Merge into {Name(merge.Groups["table"].Value)}";
        }

        if (Select().IsMatch(sql) && From().Match(sql) is { Success: true } from)
        {
            return $"Read from {Name(from.Groups["table"].Value)}{filter}";
        }

        return sql.Length <= _plainLength ? sql : sql[.._plainLength].TrimEnd() + "…";
    }

    private static string Filter(string sql) =>
        Where().Match(sql) is { Success: true } where ? $" by {Name(where.Groups["column"].Value)}" : "";

    // dbo.[Orders] becomes Orders; a schema other than dbo stays: sales.Orders.
    private static string Name(string name)
    {
        var parts = name.Replace("[", "", StringComparison.Ordinal)
            .Replace("]", "", StringComparison.Ordinal)
            .Replace("\"", "", StringComparison.Ordinal)
            .Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return name;
        }

        return parts.Length >= 2 && !parts[^2].Equals("dbo", StringComparison.OrdinalIgnoreCase)
            ? $"{parts[^2]}.{parts[^1]}"
            : parts[^1];
    }

    private static string Lower(string words) =>
        words.Length == 0 ? words : char.ToLowerInvariant(words[0]) + words[1..];

    [GeneratedRegex(@"/\*.*?\*/|--[^\n]*", RegexOptions.Singleline)]
    private static partial Regex Comments();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();

    [GeneratedRegex(@"^BEGIN\s+TRAN(SACTION)?\b[^;]*?;?", RegexOptions.IgnoreCase)]
    private static partial Regex BeginTransaction();

    // A table or procedure name: up to three parts, each plain or in brackets, such as [dbo].[Orders] or #work.
    private const string _object = @"(\[[^\]]+\]|[\w#@]+)(\s*\.\s*(\[[^\]]+\]|[\w#@]+)){0,2}";

    private const string _top = @"(TOP\s*\(\s*\d+\s*\)\s+)?";

    [GeneratedRegex(@"^(EXEC(UTE)?\s+(@\w+\s*=\s*)?)(?<name>" + _object + ")", RegexOptions.IgnoreCase)]
    private static partial Regex Procedure();

    [GeneratedRegex(@"^UPDATE\s+" + _top + "(?<table>" + _object + ")", RegexOptions.IgnoreCase)]
    private static partial Regex Update();

    [GeneratedRegex(@"^DELETE\s+" + _top + @"(FROM\s+)?(?<table>" + _object + ")", RegexOptions.IgnoreCase)]
    private static partial Regex Delete();

    [GeneratedRegex(@"^INSERT\s+(INTO\s+)?(?<table>" + _object + ")", RegexOptions.IgnoreCase)]
    private static partial Regex Insert();

    [GeneratedRegex(@"^MERGE\s+" + _top + @"(INTO\s+)?(?<table>" + _object + ")", RegexOptions.IgnoreCase)]
    private static partial Regex Merge();

    [GeneratedRegex(@"^(\(\s*)*(WITH\b.*?\)\s*)?SELECT\b", RegexOptions.IgnoreCase)]
    private static partial Regex Select();

    [GeneratedRegex(@"\bFROM\s+(?<table>" + _object + ")", RegexOptions.IgnoreCase)]
    private static partial Regex From();

    [GeneratedRegex(
        @"\bWHERE\s+(\(\s*)*(\w+\.)?(?<column>\[[^\]]+\]|\w+)\s*(=|<|>|IN\b|LIKE\b|BETWEEN\b)",
        RegexOptions.IgnoreCase)]
    private static partial Regex Where();
}
