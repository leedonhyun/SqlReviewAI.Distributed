using SqlReviewAI.Contracts;
using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Grains;

/// <summary>Keeps SqlReviewAI.Core free of any Orleans dependency by doing
/// the DTO &lt;-&gt; domain-model conversion entirely on the Grains side.</summary>
internal static class Mapping
{
    public static SqlFeaturesDto ToDto(this SqlFeatures f) => new()
    {
        StatementType = f.StatementType,
        PrimaryTable = f.PrimaryTable,
        HasWhereClause = f.HasWhereClause,
        WhereColumns = f.WhereColumns,
        SelectsAllColumns = f.SelectsAllColumns,
        HasNoLockHint = f.HasNoLockHint,
        JoinTypes = f.JoinTypes,
        UpdatedColumns = f.UpdatedColumns,
        RawSql = f.RawSql,
        NormalizedSql = f.NormalizedSql,
        SourceFile = f.SourceFile,
    };

    public static SqlFeatures ToCore(this SqlFeaturesDto d) => new()
    {
        StatementType = d.StatementType,
        PrimaryTable = d.PrimaryTable,
        HasWhereClause = d.HasWhereClause,
        WhereColumns = d.WhereColumns,
        SelectsAllColumns = d.SelectsAllColumns,
        HasNoLockHint = d.HasNoLockHint,
        JoinTypes = d.JoinTypes,
        UpdatedColumns = d.UpdatedColumns,
        RawSql = d.RawSql,
        NormalizedSql = d.NormalizedSql,
        SourceFile = d.SourceFile,
    };

    public static SeverityDto ToDto(this Severity s) => (SeverityDto)(int)s;
    public static Severity ToCore(this SeverityDto s) => (Severity)(int)s;

    public static RuleFindingDto ToDto(this RuleFinding f) =>
        new(f.RuleCode, f.Severity.ToDto(), f.Title, f.Detail, f.Evidence, f.SampleSize);

    public static RuleFinding ToCore(this RuleFindingDto f) =>
        new(f.RuleCode, f.Severity.ToCore(), f.Title, f.Detail, f.Evidence, f.SampleSize);

    public static SimilarExampleDto ToDto(this SimilarExample s) =>
        new(s.SourceFile, s.SimilarityScore, s.Sql);

    public static RiskLevelDto ToDto(this RiskLevel r) => (RiskLevelDto)(int)r;

    public static ReviewResultDto ToDto(this ReviewResult r) => new(
        r.Features.ToDto(),
        r.Score,
        r.RiskLevel.ToDto(),
        r.Findings.Select(f => f.ToDto()).ToList(),
        r.Explanation,
        r.SimilarExamples.Select(s => s.ToDto()).ToList()
    );

    public static CorpusStatisticsDto ToDto(this CorpusStatistics s) => new(
        s.TotalStatements,
        s.ByTableAndStatement.ToDictionary(
            kv => kv.Key,
            kv => new TableStatementStatsDto(kv.Value.Table, kv.Value.StatementType, kv.Value.TotalCount, kv.Value.WithWhereCount, kv.Value.SelectStarCount, kv.Value.NoLockCount)),
        s.JoinTypeCounts.ToDictionary(kv => kv.Key, kv => kv.Value),
        s.DeletePatternByTable.ToDictionary(
            kv => kv.Key,
            kv => new DeletePatternStatsDto(kv.Value.Table, kv.Value.SoftDeleteUpdateCount, kv.Value.HardDeleteCount)),
        s.SelectStatementCount,
        s.SelectStarCount,
        s.SelectWithNoLockCount
    );

    public static CorpusStatistics ToCore(this CorpusStatisticsDto d)
    {
        var stats = new CorpusStatistics { TotalStatements = d.TotalStatements };
        foreach (var (key, v) in d.ByTableAndStatement)
        {
            stats.ByTableAndStatement[key] = new TableStatementStats
            {
                Table = v.Table,
                StatementType = v.StatementType,
                TotalCount = v.TotalCount,
                WithWhereCount = v.WithWhereCount,
                SelectStarCount = v.SelectStarCount,
                NoLockCount = v.NoLockCount,
            };
        }
        foreach (var (key, v) in d.JoinTypeCounts) stats.JoinTypeCounts[key] = v;
        foreach (var (key, v) in d.DeletePatternByTable)
        {
            stats.DeletePatternByTable[key] = new DeletePatternStats
            {
                Table = v.Table,
                SoftDeleteUpdateCount = v.SoftDeleteUpdateCount,
                HardDeleteCount = v.HardDeleteCount,
            };
        }
        stats.SelectStatementCount = d.SelectStatementCount;
        stats.SelectStarCount = d.SelectStarCount;
        stats.SelectWithNoLockCount = d.SelectWithNoLockCount;
        return stats;
    }
}
