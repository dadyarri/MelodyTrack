using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MelodyTrack.Data.Telemetry;

internal static partial class PostgreSqlTraceNaming
{
    private static readonly (string Operation, Regex Pattern)[] OperationPatterns =
    [
        ("INSERT", InsertPattern()),
        ("UPDATE", UpdatePattern()),
        ("DELETE", DeletePattern()),
        ("SELECT", SelectPattern()),
        ("CREATE TABLE", CreateTablePattern()),
        ("CREATE INDEX", CreateIndexPattern()),
        ("ALTER TABLE", AlterTablePattern()),
        ("DROP TABLE", DropTablePattern()),
        ("TRUNCATE", TruncatePattern())
    ];

    public static string GetCommandSpanName(string commandText) => Parse(commandText).SpanName;

    public static string GetBatchSpanName(IEnumerable<string> commandTexts)
    {
        var labels = commandTexts.Select(Parse).ToList();
        if (labels.Count == 0)
        {
            return "BATCH";
        }

        var operations = labels
            .Select(label => label.Operation)
            .Where(operation => operation is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var collections = labels
            .Select(label => label.Collection)
            .Where(collection => collection is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return (operations, collections) switch
        {
            ([var operation], [var collection]) => $"BATCH {operation} {collection}",
            ([var operation], _) => $"BATCH {operation}",
            _ => "BATCH"
        };
    }

    public static void Enrich(Activity activity, string commandText)
    {
        var label = Parse(commandText);
        if (label.Operation is not null)
        {
            activity.SetTag("db.operation.name", label.Operation);
        }

        if (label.Collection is not null)
        {
            activity.SetTag("db.collection.name", label.Collection);
        }
    }

    public static void EnrichBatch(Activity activity, IEnumerable<string> commandTexts)
    {
        var labels = commandTexts.Select(Parse).ToList();
        var operations = labels
            .Select(label => label.Operation)
            .Where(operation => operation is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var collections = labels
            .Select(label => label.Collection)
            .Where(collection => collection is not null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (operations is [var operation])
        {
            activity.SetTag("db.operation.name", operation);
        }

        if (collections is [var collection])
        {
            activity.SetTag("db.collection.name", collection);
        }
    }

    private static DatabaseTraceLabel Parse(string commandText)
    {
        (string Operation, string Collection, int Index)? firstMatch = null;
        foreach (var (operation, pattern) in OperationPatterns)
        {
            var match = pattern.Match(commandText);
            if (!match.Success || firstMatch is not null && firstMatch.Value.Index <= match.Index)
            {
                continue;
            }

            firstMatch = (operation, NormalizeIdentifier(match.Groups["target"].Value), match.Index);
        }

        if (firstMatch is not null)
        {
            return new DatabaseTraceLabel(
                $"{firstMatch.Value.Operation} {firstMatch.Value.Collection}",
                firstMatch.Value.Operation,
                firstMatch.Value.Collection);
        }

        var fallback = FallbackOperationPattern().Match(commandText);
        if (!fallback.Success)
        {
            return new DatabaseTraceLabel("postgresql", null, null);
        }

        var fallbackOperation = fallback.Groups["operation"].Value.ToUpperInvariant();
        return new DatabaseTraceLabel(fallbackOperation, fallbackOperation, null);
    }

    private static string NormalizeIdentifier(string identifier) =>
        identifier.Replace("\"", string.Empty, StringComparison.Ordinal);

    private sealed record DatabaseTraceLabel(string SpanName, string? Operation, string? Collection);

    private const string IdentifierPattern =
        "(?<target>(?:(?:\"[^\"]+\"|[A-Za-z_][A-Za-z0-9_$]*)\\.)?(?:\"[^\"]+\"|[A-Za-z_][A-Za-z0-9_$]*))";

    [GeneratedRegex(@"\bINSERT\s+INTO\s+" + IdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InsertPattern();

    [GeneratedRegex(@"\bUPDATE\s+" + IdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpdatePattern();

    [GeneratedRegex(@"\bDELETE\s+FROM\s+" + IdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeletePattern();

    [GeneratedRegex(@"\bSELECT\b[\s\S]*?\bFROM\s+" + IdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelectPattern();

    [GeneratedRegex(@"\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?" + IdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateTablePattern();

    [GeneratedRegex(@"\bCREATE\s+(?:UNIQUE\s+)?INDEX\b[\s\S]*?\bON\s+" + IdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateIndexPattern();

    [GeneratedRegex(@"\bALTER\s+TABLE\s+" + IdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlterTablePattern();

    [GeneratedRegex(@"\bDROP\s+TABLE\s+(?:IF\s+EXISTS\s+)?" + IdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DropTablePattern();

    [GeneratedRegex(@"\bTRUNCATE\s+(?:TABLE\s+)?" + IdentifierPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TruncatePattern();

    [GeneratedRegex(@"\b(?<operation>SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|DROP|TRUNCATE|BEGIN|COMMIT|ROLLBACK|SAVEPOINT|RELEASE|SET|SHOW|COPY|CALL|DO)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FallbackOperationPattern();
}
