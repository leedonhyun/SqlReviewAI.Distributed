using System.Text;
using System.Text.RegularExpressions;
using SqlReviewAI.Core.Abstractions;
using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Extraction;

/// <summary>
/// A zero-dependency, regex/heuristic SQL feature extractor. It is
/// deliberately simple: it recognizes single top-level UPDATE / DELETE /
/// SELECT / INSERT statements well enough to drive the rule engine and
/// statistics, but it is NOT a real parser — nested subqueries, CTEs, and
/// unusual formatting can confuse it.
///
/// This exists so the whole project runs out of the box with no NuGet
/// packages at all. For production use against real, messy T-SQL, swap in
/// SqlReviewAI.ScriptDomExtraction.ScriptDomSqlFeatureExtractor, which
/// parses a real AST via Microsoft.SqlServer.TransactSql.ScriptDom — both
/// implement the same <see cref="ISqlFeatureExtractor"/> interface, so
/// nothing else in the pipeline needs to change.
/// </summary>
public sealed partial class RegexSqlFeatureExtractor : ISqlFeatureExtractor
{
    public SqlFeatures Extract(string sql, string? sourceFile = null)
    {
        var cleaned = StripComments(sql);
        var statementType = DetectStatementType(cleaned);
        var primaryTable = DetectPrimaryTable(cleaned, statementType);
        var hasWhere = WhereRegex().IsMatch(cleaned);
        var whereColumns = hasWhere ? ExtractWhereColumns(cleaned) : Array.Empty<string>();
        var selectsAll = SelectStarRegex().IsMatch(cleaned);
        var hasNoLock = NoLockRegex().IsMatch(cleaned);
        var joinTypes = ExtractJoinTypes(cleaned);
        var updatedColumns = statementType == "UPDATE" ? ExtractSetColumns(cleaned) : Array.Empty<string>();

        return new SqlFeatures
        {
            StatementType = statementType,
            PrimaryTable = primaryTable,
            HasWhereClause = hasWhere,
            WhereColumns = whereColumns,
            SelectsAllColumns = selectsAll,
            HasNoLockHint = hasNoLock,
            JoinTypes = joinTypes,
            UpdatedColumns = updatedColumns,
            RawSql = sql,
            NormalizedSql = Normalize(cleaned),
            SourceFile = sourceFile,
        };
    }

    private static string DetectStatementType(string sql)
    {
        var m = LeadingKeywordRegex().Match(sql.TrimStart());
        if (!m.Success) return "UNKNOWN";
        return m.Groups[1].Value.ToUpperInvariant() switch
        {
            "SELECT" => "SELECT",
            "UPDATE" => "UPDATE",
            "DELETE" => "DELETE",
            "INSERT" => "INSERT",
            "MERGE" => "MERGE",
            _ => "UNKNOWN",
        };
    }

    private static string? DetectPrimaryTable(string sql, string statementType)
    {
        Match m = statementType switch
        {
            "UPDATE" => UpdateTableRegex().Match(sql),
            "DELETE" => DeleteTableRegex().Match(sql),
            "SELECT" => FromTableRegex().Match(sql),
            "INSERT" => InsertTableRegex().Match(sql),
            _ => Match.Empty,
        };

        if (!m.Success) return null;
        var raw = m.Groups[1].Value.Trim('[', ']', '"', '`');
        // Keep only the last segment of schema-qualified names (dbo.MEMBER -> MEMBER)
        var lastDot = raw.LastIndexOf('.');
        return lastDot >= 0 ? raw[(lastDot + 1)..] : raw;
    }

    private static IReadOnlyList<string> ExtractWhereColumns(string sql)
    {
        var whereMatch = WhereClauseBodyRegex().Match(sql);
        if (!whereMatch.Success) return Array.Empty<string>();

        var body = whereMatch.Groups[1].Value;
        var columns = new List<string>();
        foreach (Match m in ColumnComparisonRegex().Matches(body))
        {
            columns.Add(m.Groups[1].Value.Trim('[', ']', '"', '`'));
        }
        return columns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> ExtractSetColumns(string sql)
    {
        var setMatch = SetClauseBodyRegex().Match(sql);
        if (!setMatch.Success) return Array.Empty<string>();

        var body = setMatch.Groups[1].Value;
        var columns = new List<string>();
        foreach (var assignment in SplitTopLevelCommas(body))
        {
            var eq = assignment.IndexOf('=');
            if (eq <= 0) continue;
            columns.Add(assignment[..eq].Trim().Trim('[', ']', '"', '`'));
        }
        return columns;
    }

    private static IReadOnlyList<string> ExtractJoinTypes(string sql)
    {
        var joins = new List<string>();
        foreach (Match m in JoinRegex().Matches(sql))
        {
            var qualifier = m.Groups[1].Value.Trim();
            joins.Add(string.IsNullOrEmpty(qualifier) ? "INNER" : qualifier.ToUpperInvariant());
        }
        return joins;
    }

    /// <summary>Splits on commas that are not nested inside parentheses (e.g. function calls).</summary>
    private static IEnumerable<string> SplitTopLevelCommas(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(': depth++; break;
                case ')': depth--; break;
                case ',' when depth == 0:
                    yield return text[start..i];
                    start = i + 1;
                    break;
            }
        }
        yield return text[start..];
    }

    private static string StripComments(string sql)
    {
        var noBlock = BlockCommentRegex().Replace(sql, " ");
        var noLine = LineCommentRegex().Replace(noBlock, " ");
        return noLine;
    }

    private static string Normalize(string sql)
    {
        var noStrings = StringLiteralRegex().Replace(sql, "?");
        var noNumbers = NumberLiteralRegex().Replace(noStrings, "?");
        var collapsed = WhitespaceRegex().Replace(noNumbers, " ").Trim();
        return collapsed.ToUpperInvariant();
    }

    [GeneratedRegex(@"^\s*(SELECT|UPDATE|DELETE|INSERT|MERGE)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingKeywordRegex();

    [GeneratedRegex(@"\bUPDATE\s+([\w\[\]\.""`]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateTableRegex();

    [GeneratedRegex(@"\bDELETE\s+(?:FROM\s+)?([\w\[\]\.""`]+)", RegexOptions.IgnoreCase)]
    private static partial Regex DeleteTableRegex();

    [GeneratedRegex(@"\bFROM\s+([\w\[\]\.""`]+)", RegexOptions.IgnoreCase)]
    private static partial Regex FromTableRegex();

    [GeneratedRegex(@"\bINSERT\s+INTO\s+([\w\[\]\.""`]+)", RegexOptions.IgnoreCase)]
    private static partial Regex InsertTableRegex();

    [GeneratedRegex(@"\bWHERE\b", RegexOptions.IgnoreCase)]
    private static partial Regex WhereRegex();

    // Captures everything after WHERE up to (ORDER BY | GROUP BY | HAVING | end of string).
    [GeneratedRegex(@"\bWHERE\b(.*?)(?:\bORDER\s+BY\b|\bGROUP\s+BY\b|\bHAVING\b|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex WhereClauseBodyRegex();

    [GeneratedRegex(@"([\w\[\]\.""`]+)\s*(?:=|>|<|>=|<=|<>|!=|\bLIKE\b|\bIN\b)", RegexOptions.IgnoreCase)]
    private static partial Regex ColumnComparisonRegex();

    [GeneratedRegex(@"\bSET\b(.*?)(?:\bWHERE\b|\bFROM\b|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex SetClauseBodyRegex();

    [GeneratedRegex(@"\bSELECT\s+(?:DISTINCT\s+|TOP\s*\(?\d+\)?\s+)*(\*|[\w]+\.\*)", RegexOptions.IgnoreCase)]
    private static partial Regex SelectStarRegex();

    [GeneratedRegex(@"WITH\s*\(\s*NOLOCK\s*\)|\(\s*NOLOCK\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex NoLockRegex();

    [GeneratedRegex(@"\b(INNER|LEFT(?:\s+OUTER)?|RIGHT(?:\s+OUTER)?|FULL(?:\s+OUTER)?|CROSS)?\s*JOIN\b", RegexOptions.IgnoreCase)]
    private static partial Regex JoinRegex();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockCommentRegex();

    [GeneratedRegex(@"--[^\r\n]*")]
    private static partial Regex LineCommentRegex();

    [GeneratedRegex(@"'(?:[^']|'')*'")]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(@"(?<![\w])\d+(?:\.\d+)?")]
    private static partial Regex NumberLiteralRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
