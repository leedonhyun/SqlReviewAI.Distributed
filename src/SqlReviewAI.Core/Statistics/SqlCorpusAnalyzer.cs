using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Statistics;

/// <summary>
/// Turns a pile of historical SQL (already parsed into SqlFeatures) into
/// the aggregated CorpusStatistics that the rule engine compares new SQL
/// against. This is the "통계 분석 / 패턴 분석" box in the pipeline.
/// </summary>
public sealed class SqlCorpusAnalyzer
{
    /// <summary>Column-name substrings that indicate a "soft delete" flag column.
    /// Extend this list to match your own company's naming conventions.</summary>
    public static readonly string[] SoftDeleteColumnHints =
    {
        "USE_YN", "DEL_YN", "DELETED", "IS_DELETED", "ACTIVE_YN", "STATUS", "ENABLED",
    };

    public CorpusStatistics Analyze(IEnumerable<SqlFeatures> corpus)
    {
        var stats = new CorpusStatistics();

        foreach (var f in corpus)
        {
            stats.TotalStatements++;

            foreach (var joinType in f.JoinTypes)
            {
                var key = joinType.ToUpperInvariant();
                stats.JoinTypeCounts[key] = stats.JoinTypeCounts.GetValueOrDefault(key, 0) + 1;
            }

            if (f.StatementType == "SELECT")
            {
                stats.SelectStatementCount++;
                if (f.SelectsAllColumns) stats.SelectStarCount++;
                if (f.HasNoLockHint) stats.SelectWithNoLockCount++;
            }

            if (string.IsNullOrWhiteSpace(f.PrimaryTable)) continue;

            var key2 = CorpusStatistics.Key(f.PrimaryTable, f.StatementType);
            if (!stats.ByTableAndStatement.TryGetValue(key2, out var ts))
            {
                ts = new TableStatementStats { Table = f.PrimaryTable, StatementType = f.StatementType };
                stats.ByTableAndStatement[key2] = ts;
            }

            ts.TotalCount++;
            if (f.HasWhereClause) ts.WithWhereCount++;
            if (f.SelectsAllColumns) ts.SelectStarCount++;
            if (f.HasNoLockHint) ts.NoLockCount++;

            if (f.StatementType is "UPDATE" or "DELETE")
            {
                var tableKey = f.PrimaryTable.ToUpperInvariant();
                if (!stats.DeletePatternByTable.TryGetValue(tableKey, out var dp))
                {
                    dp = new DeletePatternStats { Table = f.PrimaryTable };
                    stats.DeletePatternByTable[tableKey] = dp;
                }

                if (f.StatementType == "UPDATE" && IsSoftDeleteUpdate(f)) dp.SoftDeleteUpdateCount++;
                if (f.StatementType == "DELETE") dp.HardDeleteCount++;
            }
        }

        return stats;
    }

    private static bool IsSoftDeleteUpdate(SqlFeatures f)
    {
        return f.UpdatedColumns.Any(col =>
            SoftDeleteColumnHints.Any(hint => col.Contains(hint, StringComparison.OrdinalIgnoreCase)));
    }
}
