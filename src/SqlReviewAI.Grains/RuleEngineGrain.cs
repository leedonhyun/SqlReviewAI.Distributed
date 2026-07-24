using Orleans;
using Orleans.Concurrency;
using SqlReviewAI.Contracts;
using SqlReviewAI.Core.Rules;

namespace SqlReviewAI.Grains;

/// <summary>
/// 명시적 규칙 검사. Pure function of its two arguments — no per-key state —
/// so it's marked [StatelessWorker], letting Orleans spin up multiple
/// concurrent activations per silo under load instead of serializing every
/// call through one activation.
/// </summary>
[StatelessWorker]
public sealed class RuleEngineGrain : Grain, IRuleEngineGrain
{
    private readonly RuleEngine _ruleEngine = new();

    public Task<IReadOnlyList<RuleFindingDto>> EvaluateAsync(SqlFeaturesDto features, CorpusStatisticsDto stats)
    {
        var findings = _ruleEngine.Evaluate(features.ToCore(), stats.ToCore());
        IReadOnlyList<RuleFindingDto> result = findings.Select(f => f.ToDto()).ToList();
        return Task.FromResult(result);
    }
}
