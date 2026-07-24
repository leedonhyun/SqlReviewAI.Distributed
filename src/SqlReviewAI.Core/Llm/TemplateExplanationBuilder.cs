using System.Text;
using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Llm;

/// <summary>
/// Builds a plain-language Korean explanation directly from the rule
/// findings, with no LLM call at all. Used whenever no IChatLlmProvider is
/// configured (e.g. Ollama isn't running), so the tool always produces a
/// complete, readable report — the LLM only makes the wording more fluent,
/// it is never the only source of the explanation.
/// </summary>
public static class TemplateExplanationBuilder
{
    public static string Build(SqlFeatures features, IReadOnlyList<RuleFinding> findings)
    {
        if (findings.Count == 0)
        {
            return $"{features.PrimaryTable ?? "대상 테이블"}에 대한 {features.StatementType} 문에서 " +
                   "등록된 규칙 위반이나 이례적인 패턴이 발견되지 않았습니다.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("분석 결과:");
        sb.AppendLine();

        foreach (var f in findings.OrderByDescending(f => f.Severity))
        {
            sb.Append("- ").Append(f.Title).Append(": ").Append(f.Detail);
            if (!string.IsNullOrWhiteSpace(f.Evidence))
            {
                sb.Append(" (").Append(f.Evidence).Append(')');
            }
            sb.AppendLine();
        }

        var worst = findings.Max(f => f.Severity);
        sb.AppendLine();
        sb.AppendLine(worst >= Severity.High
            ? "따라서 실수 가능성이 매우 높으므로 배포 전 재확인을 권장합니다."
            : "치명적인 문제는 아니지만, 회사 SQL 컨벤션과 다른 부분이 있어 검토를 권장합니다.");

        return sb.ToString().TrimEnd();
    }
}
