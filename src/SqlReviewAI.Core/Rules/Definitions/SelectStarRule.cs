using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Rules.Definitions;

/// <summary>
/// Flags `SELECT *`. Always at least a Low-severity style note (unnecessary
/// I/O, brittle to schema changes); escalated to Medium if the corpus shows
/// the company rarely does this.
/// </summary>
public sealed class SelectStarRule : IRule
{
    public string Code => "SELECT_STAR";

    private const int MinCorpusSize = 20;

    public IEnumerable<RuleFinding> Evaluate(SqlFeatures features, CorpusStatistics stats)
    {
        if (!features.SelectsAllColumns) yield break;

        var severity = Severity.Low;
        string evidence;

        if (stats.SelectStatementCount >= MinCorpusSize)
        {
            var ratio = stats.SelectStarCount / (double)stats.SelectStatementCount;
            evidence = $"SELECT 문 {stats.SelectStatementCount:N0}건 중 SELECT * 사용 {stats.SelectStarCount:N0}건({ratio * 100:F1}%)";
            if (ratio < 0.1) severity = Severity.Medium;
        }
        else
        {
            evidence = "비교 가능한 이력 데이터가 충분하지 않습니다.";
        }

        yield return new RuleFinding(
            Code,
            severity,
            "SELECT * 사용",
            "필요한 컬럼만 명시적으로 선택하는 것을 권장합니다. SELECT *는 불필요한 I/O를 유발하고 스키마 변경에 취약합니다.",
            Evidence: evidence,
            SampleSize: stats.SelectStatementCount
        );
    }
}
