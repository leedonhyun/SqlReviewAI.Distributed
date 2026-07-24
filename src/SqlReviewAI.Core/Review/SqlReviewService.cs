using SqlReviewAI.Core.Abstractions;
using SqlReviewAI.Core.Llm;
using SqlReviewAI.Core.Models;
using SqlReviewAI.Core.Rules;

namespace SqlReviewAI.Core.Review;

/// <summary>
/// The end-to-end pipeline described in the project brief:
/// SQL -&gt; AST/features -&gt; rule engine + corpus statistics -&gt; RAG similarity
/// search -&gt; score/risk -&gt; LLM (or template) explanation.
/// </summary>
public sealed class SqlReviewService
{
    private readonly ISqlFeatureExtractor _extractor;
    private readonly RuleEngine _ruleEngine;
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private readonly IChatLlmProvider? _llm;

    /// <summary>Cosine similarity above this is treated as "the same SQL already exists".</summary>
    private const double DuplicateSimilarityThreshold = 0.98;

    public SqlReviewService(
        ISqlFeatureExtractor extractor,
        RuleEngine ruleEngine,
        IEmbeddingProvider embeddings,
        IVectorStore vectorStore,
        IChatLlmProvider? llm = null)
    {
        _extractor = extractor;
        _ruleEngine = ruleEngine;
        _embeddings = embeddings;
        _vectorStore = vectorStore;
        _llm = llm;
    }

    public async Task<ReviewResult> ReviewAsync(string sql, CorpusStatistics stats, CancellationToken ct = default)
    {
        var features = _extractor.Extract(sql);
        var findings = new List<RuleFinding>(_ruleEngine.Evaluate(features, stats));

        var vector = await _embeddings.EmbedAsync(features.NormalizedSql, ct);
        var neighbors = _vectorStore.Search(vector, topK: 3);

        var duplicate = neighbors.FirstOrDefault(n => n.Score >= DuplicateSimilarityThreshold);
        if (duplicate.Features is not null)
        {
            findings.Add(new RuleFinding(
                "DUPLICATE_SQL",
                Severity.Info,
                "동일/유사 SQL이 기존 시스템에 존재함",
                $"기존 SQL({duplicate.Features.SourceFile ?? "corpus"})과 매우 유사합니다. 새로 작성하기보다 기존 쿼리를 재사용하는 것을 검토하세요.",
                Evidence: $"유사도 {duplicate.Score:P1}",
                SampleSize: 1
            ));
        }

        var similarExamples = neighbors
            .Where(n => n.Score > 0.3)
            .Select(n => new SimilarExample(n.Features.SourceFile ?? "corpus", n.Score, n.Features.RawSql))
            .ToList();

        var score = ScoreCalculator.ComputeScore(findings);
        var risk = ScoreCalculator.ToRiskLevel(score, findings);

        var explanation = _llm is not null
            ? await _llm.CompleteAsync(ReviewPromptBuilder.SystemPrompt, ReviewPromptBuilder.BuildUserPrompt(features, findings, similarExamples), ct)
            : TemplateExplanationBuilder.Build(features, findings);

        return new ReviewResult(features, score, risk, findings, explanation, similarExamples);
    }

    /// <summary>Indexes one corpus statement into the vector store (call once per corpus item at startup).</summary>
    public async Task IndexAsync(string id, SqlFeatures features, CancellationToken ct = default)
    {
        var vector = await _embeddings.EmbedAsync(features.NormalizedSql, ct);
        _vectorStore.Add(id, vector, features);
    }

    /// <summary>
    /// Freeform Q&amp;A grounded in a specific SQL's review context — e.g.
    /// "이 SQL은 어느 업무 모듈과 가장 비슷한가?". Falls back to a short
    /// notice if no LLM is configured, since template-based Q&amp;A isn't
    /// meaningful for open-ended questions.
    /// </summary>
    public async Task<string> AskAsync(string question, ReviewResult context, CancellationToken ct = default)
    {
        if (_llm is null)
        {
            return "자유 질의응답에는 LLM 연동(Ollama)이 필요합니다. --ollama-url 옵션을 지정해주세요.";
        }

        var prompt = ReviewPromptBuilder.BuildUserPrompt(context.Features, context.Findings, context.SimilarExamples)
                     + "\n\n## 사용자 질문\n" + question;

        return await _llm.CompleteAsync(ReviewPromptBuilder.SystemPrompt, prompt, ct);
    }
}
