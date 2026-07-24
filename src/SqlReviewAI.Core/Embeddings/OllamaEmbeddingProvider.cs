using System.Net.Http.Json;
using SqlReviewAI.Core.Abstractions;

namespace SqlReviewAI.Core.Embeddings;

/// <summary>
/// Calls a local (or remote) Ollama server's `/api/embeddings` endpoint.
/// See https://github.com/ollama/ollama/blob/main/docs/api.md#generate-embeddings
/// </summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaEmbeddingProvider(HttpClient http, string model = "nomic-embed-text")
    {
        _http = http;
        _model = model;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/embed", new
        {
            model = _model,
            prompt = text,
        }, ct);

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: ct);
        return body?.Embeddings ?? Array.Empty<float>();
    }

    private sealed class OllamaEmbeddingResponse
    {
        public float[]? Embeddings { get; set; }
        //public List<List<float>> Embeddings { get; set; }
    }
}
