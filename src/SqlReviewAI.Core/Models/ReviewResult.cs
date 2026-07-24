namespace SqlReviewAI.Core.Models;

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical,
}

public sealed record SimilarExample(string SourceFile, double SimilarityScore, string Sql);

public sealed record ReviewResult(
    SqlFeatures Features,
    int Score,
    RiskLevel RiskLevel,
    IReadOnlyList<RuleFinding> Findings,
    string Explanation,
    IReadOnlyList<SimilarExample> SimilarExamples
);
