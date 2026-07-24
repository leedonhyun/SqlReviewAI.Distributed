namespace SqlReviewAI.Core.Models;

public enum Severity
{
    Info = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    Critical = 5,
}

/// <summary>
/// One thing a rule (or the RAG duplicate-check) found wrong, or worth
/// flagging, about a SQL statement. `Evidence` is a short, human-readable
/// statistic ("MEMBER UPDATE 중 99.4%가 WHERE 절 사용") that justifies the
/// finding using the historical corpus rather than a hard-coded opinion.
/// </summary>
public sealed record RuleFinding(
    string RuleCode,
    Severity Severity,
    string Title,
    string Detail,
    string? Evidence = null,
    int SampleSize = 0
);
