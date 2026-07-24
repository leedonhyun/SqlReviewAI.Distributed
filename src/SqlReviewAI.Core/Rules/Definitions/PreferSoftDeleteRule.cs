using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Rules.Definitions;

/// <summary>
/// If the corpus shows that a table is almost always "deleted" via an
/// UPDATE ... SET USE_YN='N' (or similar) rather than a real DELETE, flag
/// a hard DELETE against that table.
/// </summary>
public sealed class PreferSoftDeleteRule : IRule
{
    public string Code => "PREFER_SOFT_DELETE";

    private const int MinSampleSize = 5;
    private const double PreferenceThreshold = 0.9;

    public IEnumerable<RuleFinding> Evaluate(SqlFeatures features, CorpusStatistics stats)
    {
        if (features.StatementType != "DELETE") yield break;

        var pattern = stats.LookupDeletePattern(features.PrimaryTable);
        if (pattern is null) yield break;

        var total = pattern.SoftDeleteUpdateCount + pattern.HardDeleteCount;
        if (total < MinSampleSize) yield break;
        if (pattern.SoftDeletePreferenceRatio < PreferenceThreshold) yield break;

        var softPct = pattern.SoftDeletePreferenceRatio * 100;
        var hardPct = 100 - softPct;

        yield return new RuleFinding(
            Code,
            Severity.High,
            "Soft Delete 컨벤션과 불일치",
            $"{features.PrimaryTable} 테이블은 대부분 Soft Delete(UPDATE ... SET 사용 여부 컬럼)를 사용합니다. " +
            "DELETE 문 대신 상태 컬럼을 업데이트하는 방식을 검토하세요.",
            Evidence: $"DELETE 사용 빈도 {hardPct:F1}% / Soft-Delete UPDATE 사용 빈도 {softPct:F1}% ({total:N0}건 기준)",
            SampleSize: total
        );
    }
}
