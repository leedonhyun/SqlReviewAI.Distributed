using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Rules.Definitions;

/// <summary>
/// Flags UPDATE/DELETE statements with no WHERE clause — the single most
/// common way to accidentally touch every row in a table. Severity and
/// wording are driven by how consistently the historical corpus uses a
/// WHERE clause for that exact table + statement type, not a fixed opinion.
/// </summary>
public sealed class MissingWhereRule : IRule
{
    public string Code => "MISSING_WHERE";

    private const int MinSampleSize = 5;

    public IEnumerable<RuleFinding> Evaluate(SqlFeatures features, CorpusStatistics stats)
    {
        if (features.HasWhereClause) yield break;
        if (features.StatementType is not ("UPDATE" or "DELETE")) yield break;

        var tableStats = stats.Lookup(features.PrimaryTable, features.StatementType);

        // With no history to compare against, this is still worth a
        // baseline warning — just without a corpus-backed percentage.
        if (tableStats is null || tableStats.TotalCount < MinSampleSize)
        {
            yield return new RuleFinding(
                Code,
                Severity.High,
                "WHERE 절 없는 " + features.StatementType,
                $"{features.PrimaryTable ?? "대상 테이블"}에 대한 {features.StatementType} 문에 WHERE 절이 없어 전체 행이 영향을 받을 수 있습니다.",
                Evidence: "이 테이블/문 유형에 대한 비교 가능한 이력 데이터가 충분하지 않습니다.",
                SampleSize: tableStats?.TotalCount ?? 0
            );
            yield break;
        }

        var pct = tableStats.WhereUsageRatio * 100;
        var severity = pct switch
        {
            >= 95 => Severity.Critical,
            >= 80 => Severity.High,
            >= 50 => Severity.Medium,
            _ => Severity.Low,
        };

        yield return new RuleFinding(
            Code,
            severity,
            "WHERE 절 없는 " + features.StatementType,
            $"대부분의 업무 SQL은 WHERE 절을 포함합니다. 현재 SQL은 전체 테이블을 대상으로 {features.StatementType}할 가능성이 있습니다.",
            Evidence: $"{features.PrimaryTable} {features.StatementType} {tableStats.TotalCount:N0}건 중 {tableStats.WithWhereCount:N0}건({pct:F1}%)이 WHERE 절을 사용했습니다.",
            SampleSize: tableStats.TotalCount
        );
    }
}
