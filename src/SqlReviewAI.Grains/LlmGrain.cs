using Orleans;
using Orleans.Concurrency;
using SqlReviewAI.Contracts;
using SqlReviewAI.Core.Abstractions;
using SqlReviewAI.Core.Llm;

namespace SqlReviewAI.Grains;

/// <summary>
/// Ollama/Qwen3 호출. Each call is an independent HTTP round-trip with no
/// shared state, so this is a [StatelessWorker] — Orleans can run several
/// activations concurrently per silo instead of queuing every explanation
/// request through a single activation.
/// </summary>
[StatelessWorker]
public sealed class LlmGrain : Grain, ILlmGrain
{
    private readonly IChatLlmProvider? _llm;
    private readonly IStreamingChatLlmProvider? _streamingLlm;

    public LlmGrain(IChatLlmProvider? llm = null, IStreamingChatLlmProvider? streamingLlm = null)
    {
        _llm = llm;
        _streamingLlm = streamingLlm;
    }

    public Task<string> ExplainAsync(SqlFeaturesDto features, IReadOnlyList<RuleFindingDto> findings, IReadOnlyList<SimilarExampleDto> similar)
    {
        var (system, user, coreFeatures, coreFindings) = BuildPrompt(features, findings, similar);

        if (_llm is null)
        {
            return Task.FromResult(TemplateExplanationBuilder.Build(coreFeatures, coreFindings));
        }

        return _llm.CompleteAsync(system, user);
    }

    public async IAsyncEnumerable<string> ExplainStreamAsync(
        SqlFeaturesDto features, IReadOnlyList<RuleFindingDto> findings, IReadOnlyList<SimilarExampleDto> similar)
    {
        var (system, user, coreFeatures, coreFindings) = BuildPrompt(features, findings, similar);

        if (_streamingLlm is not null)
        {
            await foreach (var chunk in _streamingLlm.CompleteStreamAsync(system, user))
            {
                yield return chunk;
            }
            yield break;
        }

        // No LLM configured — fall back to the deterministic template,
        // chunked word-by-word so the "streaming" channel still behaves
        // like a stream to whatever is consuming it (e.g. a SignalR client
        // expecting incremental tokens).
        var text = TemplateExplanationBuilder.Build(coreFeatures, coreFindings);
        foreach (var word in text.Split(' '))
        {
            yield return word + " ";
        }
    }

    public Task<string> AskAsync(SqlFeaturesDto features, IReadOnlyList<RuleFindingDto> findings, IReadOnlyList<SimilarExampleDto> similar, string question)
    {
        if (_llm is null)
        {
            return Task.FromResult("자유 질의응답에는 LLM 연동(Ollama)이 필요합니다.");
        }

        var (system, user, _, _) = BuildPrompt(features, findings, similar);
        return _llm.CompleteAsync(system, user + "\n\n## 사용자 질문\n" + question);
    }

    private static (string System, string User, Core.Models.SqlFeatures Features, IReadOnlyList<Core.Models.RuleFinding> Findings) BuildPrompt(
        SqlFeaturesDto features, IReadOnlyList<RuleFindingDto> findings, IReadOnlyList<SimilarExampleDto> similar)
    {
        var coreFeatures = features.ToCore();
        var coreFindings = findings.Select(f => f.ToCore()).ToList();
        var coreSimilar = similar.Select(s => new Core.Models.SimilarExample(s.SourceFile, s.SimilarityScore, s.Sql)).ToList();

        var user = ReviewPromptBuilder.BuildUserPrompt(coreFeatures, coreFindings, coreSimilar);
        return (ReviewPromptBuilder.SystemPrompt, user, coreFeatures, coreFindings);
    }
}
