using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Core.Abstractions;

/// <summary>
/// Turns raw SQL text into a <see cref="SqlFeatures"/> summary.
/// Two implementations ship with this project:
///   - SqlReviewAI.Core.Extraction.RegexSqlFeatureExtractor
///     (zero dependencies, heuristic, works out of the box)
///   - SqlReviewAI.ScriptDomExtraction.ScriptDomSqlFeatureExtractor
///     (production-grade, real T-SQL AST via Microsoft.SqlServer.TransactSql.ScriptDom)
/// Swap the implementation in Program.cs's composition root; nothing else
/// in the pipeline needs to change.
/// </summary>
public interface ISqlFeatureExtractor
{
    SqlFeatures Extract(string sql, string? sourceFile = null);
}

/// <summary>Produces a numeric embedding vector for a piece of text.</summary>
public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}

/// <summary>A minimal vector index: add vectors, and find the nearest ones to a query vector.</summary>
public interface IVectorStore
{
    void Add(string id, float[] vector, SqlFeatures features);

    IReadOnlyList<(string Id, double Score, SqlFeatures Features)> Search(float[] queryVector, int topK);
}

/// <summary>
/// Asks an LLM to explain, in natural language, why a SQL statement was
/// flagged the way it was. Implementations receive fully-assembled context
/// (rule findings + corpus statistics + similar historical examples) so the
/// model only has to explain, never guess at company conventions.
/// </summary>
public interface IChatLlmProvider
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

/// <summary>Optional companion to IChatLlmProvider for callers that want
/// token-by-token output (e.g. to relay over a SignalR streaming hub method
/// or a Nerdbank.Streams channel) instead of waiting for the full response.</summary>
public interface IStreamingChatLlmProvider
{
    IAsyncEnumerable<string> CompleteStreamAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);
}
