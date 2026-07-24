using Orleans;

namespace SqlReviewAI.Contracts;

/// <summary>
/// SQL 리뷰 오케스트레이션. Keyed by corpus/tenant id (e.g. "default",
/// or one per team/DB). Coordinates CorpusStatsGrain, RuleEngineGrain,
/// RagGrain and LlmGrain, then returns (or streams) the aggregated result.
/// </summary>
public interface ISqlReviewGrain : IGrainWithStringKey
{
    Task<ReviewResultDto> ReviewAsync(string sql);

    /// <summary>
    /// Same review, but yields a ReviewProgressEvent as each stage
    /// completes (rule findings as they're evaluated, RAG hits, then LLM
    /// explanation tokens one at a time) instead of waiting for the whole
    /// pipeline. Requires Orleans grain-call streaming support (Orleans
    /// 7.2+): the interface simply returns IAsyncEnumerable&lt;T&gt;.
    /// </summary>
    IAsyncEnumerable<ReviewProgressEvent> ReviewStreamAsync(string sql);

    Task<string> AskAsync(string sql, string question);
}

/// <summary>
/// 회사 SQL 통계/관례. Keyed by corpus id. Holds the aggregated
/// CorpusStatistics for that corpus in grain state (single-activation —
/// Orleans guarantees only one active instance per key cluster-wide, so
/// concurrent ingests are naturally serialized).
/// </summary>
public interface ICorpusStatsGrain : IGrainWithStringKey
{
    /// <summary>Parses and folds a batch of historical SQL statements into this corpus's statistics.</summary>
    Task IngestAsync(IReadOnlyList<RawSqlEntry> entries);

    Task<CorpusStatisticsDto> GetStatisticsAsync();

    Task<int> GetStatementCountAsync();
}

[GenerateSerializer]
public sealed record RawSqlEntry([property: Id(0)] string SourceFile, [property: Id(1)] string Sql);

/// <summary>
/// 명시적 규칙 검사. Pure function of (features, stats) -&gt; findings, so it
/// is marked [StatelessWorker] in the implementation — Orleans may run
/// several activations per silo to fan out throughput, since there is no
/// per-key state to protect.
/// </summary>
public interface IRuleEngineGrain : IGrainWithIntegerKey
{
    Task<IReadOnlyList<RuleFindingDto>> EvaluateAsync(SqlFeaturesDto features, CorpusStatisticsDto stats);
}

/// <summary>
/// ChromaDB (or in-memory) + Embedding 검색. Keyed by corpus id — the
/// vector index is per-corpus state, so (unlike RuleEngineGrain) this one
/// is a normal single-activation grain.
/// </summary>
public interface IRagGrain : IGrainWithStringKey
{
    Task IndexAsync(string id, SqlFeaturesDto features);

    Task<IReadOnlyList<SimilarExampleDto>> SearchAsync(SqlFeaturesDto features, int topK);

    Task<int> GetIndexedCountAsync();
}

/// <summary>
/// Ollama/Qwen3 호출. Stateless — safe to run as [StatelessWorker] for
/// concurrency, since each call is an independent HTTP round-trip to
/// Ollama with no shared state.
/// </summary>
public interface ILlmGrain : IGrainWithIntegerKey
{
    Task<string> ExplainAsync(SqlFeaturesDto features, IReadOnlyList<RuleFindingDto> findings, IReadOnlyList<SimilarExampleDto> similar);

    IAsyncEnumerable<string> ExplainStreamAsync(SqlFeaturesDto features, IReadOnlyList<RuleFindingDto> findings, IReadOnlyList<SimilarExampleDto> similar);

    Task<string> AskAsync(SqlFeaturesDto features, IReadOnlyList<RuleFindingDto> findings, IReadOnlyList<SimilarExampleDto> similar, string question);
}
