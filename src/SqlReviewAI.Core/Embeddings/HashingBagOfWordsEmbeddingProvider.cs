using System.Text.RegularExpressions;
using SqlReviewAI.Core.Abstractions;

namespace SqlReviewAI.Core.Embeddings;

/// <summary>
/// A zero-dependency, zero-network "embedding" provider: hashes each token
/// into a fixed-size bucket (the classic hashing trick) and L2-normalizes
/// the result. It is not a semantic embedding — it will not know that
/// "회원" and "MEMBER" are related — but it is enough to catch near-exact
/// and lightly-edited duplicate SQL, and it lets the whole project run
/// with no external services.
///
/// Swap in <see cref="OllamaEmbeddingProvider"/> for real semantic
/// similarity once you have Ollama (or any embeddings endpoint) available.
/// </summary>
public sealed partial class HashingBagOfWordsEmbeddingProvider : IEmbeddingProvider
{
    private const int Dimensions = 256;

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var vector = new float[Dimensions];
        foreach (var token in TokenRegex().Matches(text).Select(m => m.Value))
        {
            var bucket = Math.Abs(token.GetHashCode()) % Dimensions;
            vector[bucket] += 1f;
        }

        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
        }

        return Task.FromResult(vector);
    }

    [GeneratedRegex(@"[A-Za-z0-9_가-힣]+")]
    private static partial Regex TokenRegex();
}
