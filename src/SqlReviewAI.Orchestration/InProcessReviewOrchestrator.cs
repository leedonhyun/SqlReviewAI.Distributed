using System.Collections.Concurrent;
using System.Text.Json;
using SqlReviewAI.Core.Abstractions;
using SqlReviewAI.Core.Embeddings;
using SqlReviewAI.Core.Llm;
using SqlReviewAI.Core.Models;
using SqlReviewAI.Core.Review;
using SqlReviewAI.Core.Rules;
using SqlReviewAI.Core.Statistics;

namespace SqlReviewAI.Orchestration;

/// <summary>
/// Runs the entire pipeline (parse -&gt; rules -&gt; RAG -&gt; score -&gt; explain)
/// in-process, one <see cref="SqlReviewService"/> + isolated vector index
/// per corpus id. This is the default orchestrator so the Web app is
/// useful standalone; swap in the Orleans-backed one once a Silo cluster
/// is available (see SqlReviewAI.Web.OrleansIntegration).
/// </summary>
public sealed class InProcessReviewOrchestrator : IReviewOrchestrator
{
    private const double DuplicateSimilarityThreshold = 0.98;

    private readonly ISqlFeatureExtractor _extractor;
    private readonly Func<IEmbeddingProvider> _embeddingsFactory;
    private readonly IChatLlmProvider? _llm;
    private readonly IStreamingChatLlmProvider? _streamingLlm;
    private readonly RuleEngine _ruleEngine = new();
    private readonly SqlCorpusAnalyzer _analyzer = new();

    private readonly ConcurrentDictionary<string, CorpusEntry> _corpora = new();

    public InProcessReviewOrchestrator(
        ISqlFeatureExtractor extractor,
        Func<IEmbeddingProvider> embeddingsFactory,
        IChatLlmProvider? llm = null,
        IStreamingChatLlmProvider? streamingLlm = null)
    {
        _extractor = extractor;
        _embeddingsFactory = embeddingsFactory;
        _llm = llm;
        _streamingLlm = streamingLlm;
    }

    public async Task IngestCorpusAsync(string corpusId, IReadOnlyList<(string SourceFile, string Sql)> entries, CancellationToken ct)
    {
        var entry = GetOrCreateCorpus(corpusId);
        await entry.Lock.WaitAsync(ct);
        try
        {
            foreach (var (sourceFile, sql) in entries)
            {
                var features = _extractor.Extract(sql, sourceFile);
                entry.Corpus.Add(features);
                var vector = await entry.Embeddings.EmbedAsync(features.NormalizedSql, ct);
                entry.VectorStore.Add(sourceFile, vector, features);
            }
            entry.Statistics = _analyzer.Analyze(entry.Corpus);
        }
        finally
        {
            entry.Lock.Release();
        }
    }

    public Task<int> GetCorpusSizeAsync(string corpusId, CancellationToken ct) =>
        Task.FromResult(_corpora.TryGetValue(corpusId, out var e) ? e.Corpus.Count : 0);

    public async Task<ReviewResult> ReviewAsync(string corpusId, string sql, CancellationToken ct)
    {
        var entry = GetOrCreateCorpus(corpusId);
        var features = _extractor.Extract(sql);
        var findings = new List<RuleFinding>(_ruleEngine.Evaluate(features, entry.Statistics));

        var vector = await entry.Embeddings.EmbedAsync(features.NormalizedSql, ct);
        var neighbors = entry.VectorStore.Search(vector, topK: 3);
        AppendDuplicateFindingIfAny(neighbors, findings);

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

    public async IAsyncEnumerable<ReviewProgressEvent> ReviewStreamAsync(
        string corpusId, string sql, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var entry = GetOrCreateCorpus(corpusId);
        var features = _extractor.Extract(sql);

        yield return Log("리뷰 시작: 규칙 검사 준비 중");

        var findings = new List<RuleFinding>(_ruleEngine.Evaluate(features, entry.Statistics));
        foreach (var f in findings)
        {
            yield return new ReviewProgressEvent(ReviewChannel.Rules, "finding", JsonSerializer.Serialize(f), DateTimeOffset.UtcNow);
        }
        yield return Log($"규칙 검사 완료: {findings.Count}건 발견");

        var vector = await entry.Embeddings.EmbedAsync(features.NormalizedSql, ct);
        var neighbors = entry.VectorStore.Search(vector, topK: 3);
        AppendDuplicateFindingIfAny(neighbors, findings);

        var similarExamples = neighbors.Where(n => n.Score > 0.3).ToList();
        foreach (var s in similarExamples)
        {
            var dto = new SimilarExample(s.Features.SourceFile ?? "corpus", s.Score, s.Features.RawSql);
            yield return new ReviewProgressEvent(ReviewChannel.Rag, "similar", JsonSerializer.Serialize(dto), DateTimeOffset.UtcNow);
        }
        yield return Log($"RAG 유사도 검색 완료: {similarExamples.Count}건");

        yield return Log("LLM 설명 생성 중");
        var coreSimilar = similarExamples.Select(s => new SimilarExample(s.Features.SourceFile ?? "corpus", s.Score, s.Features.RawSql)).ToList();

        if (_streamingLlm is not null)
        {
            var prompt = ReviewPromptBuilder.BuildUserPrompt(features, findings, coreSimilar);
            await foreach (var token in _streamingLlm.CompleteStreamAsync(ReviewPromptBuilder.SystemPrompt, prompt, ct))
            {
                yield return new ReviewProgressEvent(ReviewChannel.Llm, "token", token, DateTimeOffset.UtcNow);
            }
        }
        else
        {
            var text = TemplateExplanationBuilder.Build(features, findings);
            foreach (var word in text.Split(' '))
            {
                yield return new ReviewProgressEvent(ReviewChannel.Llm, "token", word + " ", DateTimeOffset.UtcNow);
            }
        }

        yield return Log("리뷰 완료");
    }

    public async Task<string> AskAsync(string corpusId, string sql, string question, CancellationToken ct)
    {
        if (_llm is null) return "자유 질의응답에는 LLM 연동(Ollama)이 필요합니다.";

        var entry = GetOrCreateCorpus(corpusId);
        var features = _extractor.Extract(sql);
        var findings = _ruleEngine.Evaluate(features, entry.Statistics);

        var vector = await entry.Embeddings.EmbedAsync(features.NormalizedSql, ct);
        var similar = entry.VectorStore.Search(vector, topK: 3)
            .Select(n => new SimilarExample(n.Features.SourceFile ?? "corpus", n.Score, n.Features.RawSql))
            .ToList();

        var prompt = ReviewPromptBuilder.BuildUserPrompt(features, findings, similar) + "\n\n## 사용자 질문\n" + question;
        return await _llm.CompleteAsync(ReviewPromptBuilder.SystemPrompt, prompt, ct);
    }

    private CorpusEntry GetOrCreateCorpus(string corpusId) =>
        _corpora.GetOrAdd(corpusId, _ => new CorpusEntry(_embeddingsFactory()));

    private static void AppendDuplicateFindingIfAny(
        IReadOnlyList<(string Id, double Score, SqlFeatures Features)> neighbors, List<RuleFinding> findings)
    {
        var duplicate = neighbors.FirstOrDefault(n => n.Score >= DuplicateSimilarityThreshold);
        if (duplicate.Features is null) return;

        findings.Add(new RuleFinding(
            "DUPLICATE_SQL",
            Severity.Info,
            "동일/유사 SQL이 기존 시스템에 존재함",
            $"기존 SQL({duplicate.Features.SourceFile ?? "corpus"})과 매우 유사합니다. 새로 작성하기보다 기존 쿼리를 재사용하는 것을 검토하세요.",
            Evidence: $"유사도 {duplicate.Score:P1}",
            SampleSize: 1));
    }

    private static ReviewProgressEvent Log(string message) =>
        new(ReviewChannel.Logs, "info", JsonSerializer.Serialize(new { message }), DateTimeOffset.UtcNow);

    private sealed class CorpusEntry(IEmbeddingProvider embeddings)
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public List<SqlFeatures> Corpus { get; } = new();
        public CorpusStatistics Statistics { get; set; } = new();
        public IEmbeddingProvider Embeddings { get; } = embeddings;
        public InMemoryVectorStore VectorStore { get; } = new();
    }
}
