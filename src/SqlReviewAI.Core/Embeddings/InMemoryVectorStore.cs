using SqlReviewAI.Core.Abstractions;
using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Embeddings;

/// <summary>
/// A minimal, dependency-free vector index using brute-force cosine
/// similarity. Perfectly fine up to tens of thousands of corpus statements
/// (a linear scan over a few thousand short float[] vectors is well under
/// a millisecond). For a much larger corpus, implement <see cref="IVectorStore"/>
/// against a real vector database (Chroma, Qdrant, pgvector, ...) instead —
/// nothing else in the pipeline needs to change.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly List<(string Id, float[] Vector, SqlFeatures Features)> _items = new();

    public void Add(string id, float[] vector, SqlFeatures features)
    {
        _items.Add((id, vector, features));
    }

    public IReadOnlyList<(string Id, double Score, SqlFeatures Features)> Search(float[] queryVector, int topK)
    {
        return _items
            .Select(item => (item.Id, Score: CosineSimilarity(queryVector, item.Vector), item.Features))
            .OrderByDescending(r => r.Score)
            .Take(topK)
            .ToList();
    }

    public int Count => _items.Count;

    private static double CosineSimilarity(float[] a, float[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        if (normA == 0 || normB == 0) return 0;
        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
