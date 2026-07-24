using System.Text;
using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Llm;

/// <summary>
/// Assembles the prompt sent to the LLM. The LLM is given the rule
/// findings, corpus evidence, and similar historical SQL already computed
/// by the deterministic parts of the pipeline — its job is narrating
/// "왜 위험한지" / "회사 스타일과 어떻게 다른지" in fluent Korean, not
/// deciding what counts as risky.
/// </summary>
public static class ReviewPromptBuilder
{
    public static string SystemPrompt =>
        "당신은 사내 SQL 코드 리뷰를 돕는 어시스턴트입니다. " +
        "아래에 제공되는 규칙 위반 목록과 통계 근거만을 사용하여, 왜 해당 SQL이 회사 컨벤션과 다른지, " +
        "그리고 실무적으로 어떤 위험이 있는지 한국어로 간결하고 명확하게 설명하세요. " +
        "제공되지 않은 사실을 추측해서 만들어내지 마세요. 근거로 제시된 통계 수치는 그대로 인용하세요.";

    public static string BuildUserPrompt(
        SqlFeatures features,
        IReadOnlyList<RuleFinding> findings,
        IReadOnlyList<SimilarExample> similarExamples)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## 검토 대상 SQL");
        sb.AppendLine("```sql");
        sb.AppendLine(features.RawSql.Trim());
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine($"문 종류: {features.StatementType}, 대상 테이블: {features.PrimaryTable ?? "알 수 없음"}");
        sb.AppendLine();

        sb.AppendLine("## 규칙 엔진이 발견한 사항");
        if (findings.Count == 0)
        {
            sb.AppendLine("(없음 — 등록된 규칙을 위반하지 않았습니다.)");
        }
        else
        {
            foreach (var f in findings)
            {
                sb.AppendLine($"- [{f.Severity}] {f.Title}: {f.Detail}");
                if (!string.IsNullOrWhiteSpace(f.Evidence))
                {
                    sb.AppendLine($"  근거: {f.Evidence}");
                }
            }
        }
        sb.AppendLine();

        sb.AppendLine("## 유사한 기존 업무 SQL (RAG 검색 결과)");
        if (similarExamples.Count == 0)
        {
            sb.AppendLine("(유사한 사례를 찾지 못했습니다.)");
        }
        else
        {
            foreach (var ex in similarExamples)
            {
                sb.AppendLine($"- 유사도 {ex.SimilarityScore:P1} ({ex.SourceFile}):");
                sb.AppendLine("```sql");
                sb.AppendLine(ex.Sql.Trim());
                sb.AppendLine("```");
            }
        }
        sb.AppendLine();

        sb.AppendLine("위 내용을 바탕으로, 이 SQL이 왜 위험하거나 회사 표준과 다른지 3~5문장으로 설명해주세요.");

        return sb.ToString();
    }
}
