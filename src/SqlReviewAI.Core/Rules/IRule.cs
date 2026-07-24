using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Rules;

/// <summary>
/// A single, explicit, explainable check. Rules never call an LLM — they
/// look at the parsed statement and the historical corpus statistics and
/// decide, deterministically, whether something is worth flagging. This is
/// the "명확한 규칙 검사" layer; the LLM's job (elsewhere) is only to
/// narrate what the rules already found.
/// </summary>
public interface IRule
{
    string Code { get; }

    IEnumerable<RuleFinding> Evaluate(SqlFeatures features, CorpusStatistics stats);
}
