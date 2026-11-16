using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Veyra.Query.Queries.Api;

namespace Veyra.Query.Queries;

/// <summary>
/// Reads a SQL Server execution plan's XML (showplan) into what the query page shows (ADR 0020, area 4): its operators
/// with their share of the cost, and its warnings in words with what to do. Pure logic, tested without a database.
/// </summary>
/// <remarks>
/// An operator's own cost is its subtree cost less its children's. Warnings come from the plan's own
/// <c>Warnings</c> and <c>MissingIndexes</c> elements; nothing is guessed. A plan that is not XML reads as empty.
/// </remarks>
internal static class PlanReader
{
    private const int _maxOperators = 60;

    private static readonly XNamespace _plan = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    /// <summary>Reads a plan.</summary>
    /// <param name="xml">The plan's XML.</param>
    /// <returns>Its operators, the costliest kept in plan order, and its warnings.</returns>
    public static (IReadOnlyList<PlanOperatorResponse> Operators, IReadOnlyList<PlanWarningResponse> Warnings) Read(
        string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            return ([], []);
        }

        XDocument document;

        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException)
        {
            return ([], []);
        }

        var top = document.Descendants(_plan + "RelOp").FirstOrDefault();
        var operators = new List<PlanOperatorResponse>();

        if (top is not null)
        {
            var total = Number(top, "EstimatedTotalSubtreeCost");
            Walk(top, 0, total, operators);
        }

        return (operators.Take(_maxOperators).ToList(), Warnings(document));
    }

    private static void Walk(XElement node, int depth, double total, List<PlanOperatorResponse> into)
    {
        var children = Children(node).ToList();
        var own = Math.Max(
            0,
            Number(node, "EstimatedTotalSubtreeCost")
                - children.Sum(child => Number(child, "EstimatedTotalSubtreeCost")));
        var objectName = node.Elements().Descendants(_plan + "Object").FirstOrDefault() is { } target
            ? Name(target)
            : null;

        into.Add(new PlanOperatorResponse(
            depth,
            (string?)node.Attribute("PhysicalOp") ?? "Operator",
            (string?)node.Attribute("LogicalOp") ?? "",
            objectName,
            total > 0 ? own / total : 0,
            Number(node, "EstimateRows"),
            node.Element(_plan + "Warnings") is not null));

        foreach (var child in children)
        {
            Walk(child, depth + 1, total, into);
        }
    }

    // The operators directly under an operator, whatever element sits between them.
    private static IEnumerable<XElement> Children(XElement node) =>
        node.Elements().SelectMany(element => element.Name == _plan + "RelOp"
            ? [element]
            : element.Descendants(_plan + "RelOp").Where(descendant =>
                descendant.Ancestors(_plan + "RelOp").First() == node));

    private static List<PlanWarningResponse> Warnings(XDocument document)
    {
        var warnings = new List<PlanWarningResponse>();

        foreach (var convert in document.Descendants(_plan + "PlanAffectingConvert")
            .Where(convert => ((string?)convert.Attribute("ConvertIssue"))
                ?.Contains("Seek", StringComparison.Ordinal) == true)
            .GroupBy(convert => (string?)convert.Attribute("Expression") ?? "")
            .Select(group => group.First()))
        {
            var expression = (string?)convert.Attribute("Expression") ?? "";

            warnings.Add(new PlanWarningResponse(
                "implicit-conversion",
                "warning",
                $"Implicit conversion: {Shorten(expression)}",
                "A value is converted to the column's type for every row, so SQL Server cannot seek the index on that "
                + "column and reads them all.",
                "Send the parameter with the column's type from the application, such as int instead of text.",
                null));
        }

        if (document.Descendants(_plan + "SpillToTempDb").Any())
        {
            warnings.Add(new PlanWarningResponse(
                "spill",
                "caution",
                "Spills to tempdb",
                "A sort or hash had too little memory and wrote to tempdb, which is far slower.",
                "Usually the row estimate is far off: update the statistics of the tables it reads.",
                null));
        }

        if (document.Descendants(_plan + "NoJoinPredicate").Any())
        {
            warnings.Add(new PlanWarningResponse(
                "no-join-predicate",
                "warning",
                "A join without a condition",
                "Two inputs are joined without a condition, so every row meets every other row.",
                "Check the statement's JOIN conditions.",
                null));
        }

        foreach (var group in document.Descendants(_plan + "MissingIndexGroup"))
        {
            var impact = Number(group, "Impact");
            var index = group.Element(_plan + "MissingIndex");

            if (index is null)
            {
                continue;
            }

            var table = $"{(string?)index.Attribute("Schema")}.{(string?)index.Attribute("Table")}";
            string[] Columns(string usage) =>
                index.Elements(_plan + "ColumnGroup")
                    .Where(columns => (string?)columns.Attribute("Usage") == usage)
                    .SelectMany(columns => columns.Elements(_plan + "Column"))
                    .Select(column => (string?)column.Attribute("Name") ?? "")
                    .ToArray();

            var keys = Columns("EQUALITY").Concat(Columns("INEQUALITY")).ToArray();
            var include = Columns("INCLUDE");
            var name = "IX_" + Clean(((string?)index.Attribute("Table")) ?? "table") + "_"
                + string.Join("_", keys.Select(Clean));
            var script = $"CREATE INDEX {name} ON {table} ({string.Join(", ", keys)})"
                + (include.Length > 0 ? $" INCLUDE ({string.Join(", ", include)})" : "") + ";";

            warnings.Add(new PlanWarningResponse(
                "missing-index",
                impact >= 50 ? "warning" : "caution",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Missing index on {Clean(table)}, estimated gain {impact:0}%"),
                "SQL Server estimates that an index on these columns would make this statement that much cheaper.",
                "A DBA can add it; it makes reads faster and every write to the table a little slower.",
                script));
        }

        return warnings;
    }

    private static string Name(XElement target)
    {
        var schema = Clean((string?)target.Attribute("Schema") ?? "");
        var table = Clean((string?)target.Attribute("Table") ?? "");
        var index = Clean((string?)target.Attribute("Index") ?? "");

        return string.Join(".", new[] { schema, table }.Where(part => part.Length > 0 && part != "dbo"))
            + (index.Length > 0 ? $" · {index}" : "");
    }

    private static string Clean(string name) => name
        .Replace("[", "", StringComparison.Ordinal)
        .Replace("]", "", StringComparison.Ordinal);

    private static string Shorten(string text) => text.Length <= 80 ? text : text[..80] + "…";

    private static double Number(XElement element, string attribute) =>
        double.TryParse(
            (string?)element.Attribute(attribute),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
}
