using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Review;

/// <summary>
/// Turns a list of findings into a single 0-100 score and a risk bucket.
/// Deliberately simple and transparent (fixed point deductions per
/// severity) rather than a learned/opaque model — the whole point of this
/// tool is that every number is traceable back to an explicit rule.
/// </summary>
public static class ScoreCalculator
{
    private static readonly Dictionary<Severity, int> Deductions = new()
    {
        [Severity.Info] = 2,
        [Severity.Low] = 5,
        [Severity.Medium] = 12,
        [Severity.High] = 22,
        [Severity.Critical] = 35,
    };

    public static int ComputeScore(IReadOnlyList<RuleFinding> findings)
    {
        var score = 100 - findings.Sum(f => Deductions[f.Severity]);
        return Math.Clamp(score, 0, 100);
    }

    public static RiskLevel ToRiskLevel(int score, IReadOnlyList<RuleFinding> findings)
    {
        if (findings.Any(f => f.Severity == Severity.Critical) || score < 50) return RiskLevel.Critical;
        if (findings.Any(f => f.Severity == Severity.High) || score < 70) return RiskLevel.High;
        if (score < 90) return RiskLevel.Medium;
        return RiskLevel.Low;
    }
}
