using Orleans;
using SqlReviewAI.Contracts;
using SqlReviewAI.Core.Abstractions;

namespace SqlReviewAI.Grains;

/// <summary>
/// ChromaDB(또는 in-memory) + Embedding 검색. One activation per corpus id
/// — the vector index is per-corpus state that must stay consistent, so
/// (unlike RuleEngineGrain/LlmGrain) this is NOT a stateless worker.
/// The actual vector store implementation (in-memory brute-force cosine
/// similarity, or a real Chroma server over HTTP) is injected — see
/// SqlReviewAI.Core.Embeddings.InMemoryVectorStore / ChromaVectorStore.
/// </summary>
public sealed class RagGrain : Grain, IRagGrain
{
    private readonly IEmbeddingProvider _embeddings;
    private readonly IVectorStore _vectorStore;
    private int _indexedCount;

    public RagGrain(IEmbeddingProvider embeddings, IVectorStore vectorStore)
    {
        _embeddings = embeddings;
        _vectorStore = vectorStore;
    }

    public async Task IndexAsync(string id, SqlFeaturesDto features)
    {
        var core = features.ToCore();
        var vector = await _embeddings.EmbedAsync(core.NormalizedSql);
        _vectorStore.Add(id, vector, core);
        _indexedCount++;
    }

    public async Task<IReadOnlyList<SimilarExampleDto>> SearchAsync(SqlFeaturesDto features, int topK)
    {
        var vector = await _embeddings.EmbedAsync(features.ToCore().NormalizedSql);
        return _vectorStore.Search(vector, topK)
            .Select(r => new SimilarExampleDto(r.Features.SourceFile ?? "corpus", r.Score, r.Features.RawSql))
            .ToList();
    }

    public Task<int> GetIndexedCountAsync() => Task.FromResult(_indexedCount);
}
