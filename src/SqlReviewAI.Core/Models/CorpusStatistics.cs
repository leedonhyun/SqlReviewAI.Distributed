namespace SqlReviewAI.Core.Models;

/// <summary>
/// Aggregated statistics for one (table, statement type) pair across the
/// historical corpus, e.g. "MEMBER" + "UPDATE".
/// </summary>
public sealed class TableStatementStats
{
    public required string Table { get; init; }
    public required string StatementType { get; init; }

    public int TotalCount { get; set; }
    public int WithWhereCount { get; set; }
    public int SelectStarCount { get; set; }
    public int NoLockCount { get; set; }

    public double WhereUsageRatio => TotalCount == 0 ? 0 : (double)WithWhereCount / TotalCount;
}

/// <summary>
/// Per-table tally of "soft delete" (UPDATE ... SET use_yn='N') vs. real
/// DELETE statements, independent of statement type — this is what lets us
/// compare "how often is a delete-like operation done as a real DELETE"
/// for a given table.
/// </summary>
public sealed class DeletePatternStats
{
    public required string Table { get; init; }
    public int SoftDeleteUpdateCount { get; set; }
    public int HardDeleteCount { get; set; }

    public double SoftDeletePreferenceRatio
    {
        get
        {
            var total = SoftDeleteUpdateCount + HardDeleteCount;
            return total == 0 ? 0 : (double)SoftDeleteUpdateCount / total;
        }
    }
}

/// <summary>
/// Corpus-wide (not table-specific) statistics, mainly used for patterns
/// like JOIN-type distribution where "how the company writes SQL in
/// general" matters more than any one table.
/// </summary>
public sealed class CorpusStatistics
{
    public int TotalStatements { get; set; }

    /// <summary>Keyed by "{Table}|{StatementType}", e.g. "MEMBER|UPDATE".</summary>
    public Dictionary<string, TableStatementStats> ByTableAndStatement { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Corpus-wide join type usage counts, e.g. {"INNER": 4210, "LEFT": 980}.</summary>
    public Dictionary<string, int> JoinTypeCounts { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Soft-delete vs. hard-delete tally per table (see <see cref="DeletePatternStats"/>).</summary>
    public Dictionary<string, DeletePatternStats> DeletePatternByTable { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public DeletePatternStats? LookupDeletePattern(string? table)
    {
        if (string.IsNullOrWhiteSpace(table)) return null;
        return DeletePatternByTable.GetValueOrDefault(table.ToUpperInvariant());
    }

    public int SelectStatementCount { get; set; }
    public int SelectStarCount { get; set; }
    public int SelectWithNoLockCount { get; set; }

    public static string Key(string table, string statementType) => $"{table}|{statementType}".ToUpperInvariant();

    public TableStatementStats? Lookup(string? table, string statementType)
    {
        if (string.IsNullOrWhiteSpace(table)) return null;
        return ByTableAndStatement.GetValueOrDefault(Key(table, statementType));
    }

    public double JoinTypeUsageRatio(string joinType)
    {
        var total = JoinTypeCounts.Values.Sum();
        if (total == 0) return 0;
        return JoinTypeCounts.GetValueOrDefault(joinType.ToUpperInvariant(), 0) / (double)total;
    }
}
