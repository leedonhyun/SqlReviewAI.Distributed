using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Rules;

public sealed class RuleEngine
{
    private readonly IReadOnlyList<IRule> _rules;

    public RuleEngine(IEnumerable<IRule>? rules = null)
    {
        _rules = rules?.ToList() ?? DefaultRules().ToList();
    }

    /// <summary>The rule set described in the project brief: missing WHERE,
    /// soft-delete preference, unusual JOIN types, SELECT *, missing NOLOCK.</summary>
    public static IEnumerable<IRule> DefaultRules()
    {
        yield return new Definitions.MissingWhereRule();
        yield return new Definitions.PreferSoftDeleteRule();
        yield return new Definitions.UnusualJoinTypeRule();
        yield return new Definitions.SelectStarRule();
        yield return new Definitions.NoLockMissingRule();
    }

    public IReadOnlyList<RuleFinding> Evaluate(SqlFeatures features, CorpusStatistics stats)
    {
        var findings = new List<RuleFinding>();
        foreach (var rule in _rules)
        {
            findings.AddRange(rule.Evaluate(features, stats));
        }
        return findings;
    }
}
