using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Rules.Definitions;

/// <summary>
/// If the company overwhelmingly uses WITH (NOLOCK) on SELECTs against a
/// given table, flag a SELECT that omits it. (This rule intentionally does
/// NOT argue NOLOCK is good practice in general — it only checks
/// consistency with the existing codebase, since that's the convention
/// this tool exists to enforce.)
/// </summary>
public sealed class NoLockMissingRule : IRule
{
    public string Code => "NOLOCK_MISSING";

    private const int MinSampleSize = 10;
    private const double ConventionThreshold = 0.9;

    public IEnumerable<RuleFinding> Evaluate(SqlFeatures features, CorpusStatistics stats)
    {
        if (features.StatementType != "SELECT") yield break;
        if (features.HasNoLockHint) yield break;

        var tableStats = stats.Lookup(features.PrimaryTable, "SELECT");
        if (tableStats is null || tableStats.TotalCount < MinSampleSize) yield break;

        var ratio = tableStats.NoLockCount / (double)tableStats.TotalCount;
        if (ratio < ConventionThreshold) yield break;

        yield return new RuleFinding(
            Code,
            Severity.Low,
            "WITH (NOLOCK) 누락",
            $"{features.PrimaryTable} 테이블 조회는 대부분 WITH (NOLOCK) 힌트를 사용합니다. 현재 SQL에는 누락되어 있습니다.",
            Evidence: $"{features.PrimaryTable} SELECT {tableStats.TotalCount:N0}건 중 {tableStats.NoLockCount:N0}건({ratio * 100:F1}%)이 NOLOCK을 사용했습니다.",
            SampleSize: tableStats.TotalCount
        );
    }
}
