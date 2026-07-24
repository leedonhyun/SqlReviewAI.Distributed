using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Rules.Definitions;

/// <summary>
/// Flags JOIN types that the company's historical SQL almost never uses.
/// A CROSS JOIN appearing in a codebase that is otherwise 100% INNER/LEFT
/// JOIN is far more likely to be a missing join predicate than an
/// intentional cartesian product.
/// </summary>
public sealed class UnusualJoinTypeRule : IRule
{
    public string Code => "UNUSUAL_JOIN_TYPE";

    private const int MinCorpusSize = 20;
    private const double RareThreshold = 0.01; // under 1% of historical joins

    public IEnumerable<RuleFinding> Evaluate(SqlFeatures features, CorpusStatistics stats)
    {
        var totalJoins = stats.JoinTypeCounts.Values.Sum();
        if (totalJoins < MinCorpusSize) yield break;

        foreach (var joinType in features.JoinTypes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var count = stats.JoinTypeCounts.GetValueOrDefault(joinType.ToUpperInvariant(), 0);
            var ratio = count / (double)totalJoins;
            if (ratio > RareThreshold) continue;

            var severity = count == 0 ? Severity.High : Severity.Medium;
            var evidence = count == 0
                ? $"업무 SQL {totalJoins:N0}건 중 {joinType} JOIN 사용 사례가 없습니다."
                : $"업무 SQL {totalJoins:N0}건 중 {joinType} JOIN은 {count:N0}건({ratio * 100:F2}%)만 사용되었습니다.";

            var cartesianNote = joinType.Equals("CROSS", StringComparison.OrdinalIgnoreCase)
                ? " Cartesian Product(예상치 못한 행 폭증) 발생 가능성이 있습니다."
                : " 의도한 조인 조건이 맞는지 다시 확인하세요.";

            yield return new RuleFinding(
                Code,
                severity,
                $"이례적인 JOIN 유형: {joinType} JOIN",
                $"회사 SQL은 대부분 {DescribeCommonJoins(stats)} JOIN을 사용합니다. 현재 SQL의 {joinType} JOIN은 이례적입니다." + cartesianNote,
                Evidence: evidence,
                SampleSize: totalJoins
            );
        }
    }

    private static string DescribeCommonJoins(CorpusStatistics stats)
    {
        var top = stats.JoinTypeCounts
            .OrderByDescending(kv => kv.Value)
            .Take(2)
            .Select(kv => kv.Key);
        return string.Join("/", top);
    }
}
