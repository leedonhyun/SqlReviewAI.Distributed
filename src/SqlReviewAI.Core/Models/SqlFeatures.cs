namespace SqlReviewAI.Core.Models;

/// <summary>
/// A statement-type-agnostic, parser-agnostic summary of a single SQL
/// statement. This is the boundary between "how we parsed the SQL"
/// (regex heuristics vs. Microsoft.SqlServer.TransactSql.ScriptDom AST)
/// and everything downstream (rules, statistics, embeddings). Any
/// ISqlFeatureExtractor implementation only needs to produce one of these.
/// </summary>
public sealed class SqlFeatures
{
    /// <summary>"SELECT", "UPDATE", "DELETE", "INSERT", "MERGE", "UNKNOWN", ...</summary>
    public string StatementType { get; init; } = "UNKNOWN";

    /// <summary>Primary table the statement targets (best-effort; the first/only table for simple statements).</summary>
    public string? PrimaryTable { get; init; }

    /// <summary>True if the statement has a WHERE clause.</summary>
    public bool HasWhereClause { get; init; }

    /// <summary>Column names referenced in the WHERE clause, if any could be identified.</summary>
    public IReadOnlyList<string> WhereColumns { get; init; } = Array.Empty<string>();

    /// <summary>True for `SELECT *` (or `SELECT table.*`).</summary>
    public bool SelectsAllColumns { get; init; }

    /// <summary>True if any table hint includes NOLOCK.</summary>
    public bool HasNoLockHint { get; init; }

    /// <summary>Join keywords found, e.g. "INNER", "LEFT", "RIGHT", "FULL", "CROSS".</summary>
    public IReadOnlyList<string> JoinTypes { get; init; } = Array.Empty<string>();

    /// <summary>For UPDATE statements: the columns being assigned in the SET clause.</summary>
    public IReadOnlyList<string> UpdatedColumns { get; init; } = Array.Empty<string>();

    /// <summary>The original SQL text, verbatim.</summary>
    public string RawSql { get; init; } = string.Empty;

    /// <summary>Whitespace-collapsed, literal-stripped text used for embeddings / dedup.</summary>
    public string NormalizedSql { get; init; } = string.Empty;

    /// <summary>Name of the source file this statement came from, if any (for corpus provenance).</summary>
    public string? SourceFile { get; init; }
}
