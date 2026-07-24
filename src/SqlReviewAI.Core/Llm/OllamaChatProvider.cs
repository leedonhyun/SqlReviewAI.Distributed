using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using SqlReviewAI.Core.Abstractions;

namespace SqlReviewAI.Core.Llm;

/// <summary>
/// Calls a local (or remote) Ollama server's `/api/chat` endpoint —
/// e.g. running `qwen3:14b`. This is the only place in the pipeline that
/// talks to an LLM, and it is only ever asked to *explain* findings the
/// rule engine and statistics already computed — never to decide risk on
/// its own.
///
/// Implements both IChatLlmProvider (single, complete response — used by
/// the plain review pipeline) and IStreamingChatLlmProvider (token-by-token
/// — used by LlmGrain.ExplainStreamAsync, relayed to the browser over a
/// SignalR streaming hub method).
/// See https://github.com/ollama/ollama/blob/main/docs/api.md#generate-a-chat-completion
/// </summary>
public sealed class OllamaChatProvider : IChatLlmProvider, IStreamingChatLlmProvider
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaChatProvider(HttpClient http, string model = "qwen3:14b")
    {
        _http = http;
        _model = model;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/chat", BuildRequest(systemPrompt, userPrompt, stream: false), ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: ct);
        return body?.Message?.Content?.Trim() ?? string.Empty;
    }

    public async IAsyncEnumerable<string> CompleteStreamAsync(
        string systemPrompt, string userPrompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(BuildRequest(systemPrompt, userPrompt, stream: true)),
        };

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        // Ollama streams one JSON object per line (NDJSON), each carrying
        // the next chunk of the message plus a `done` flag on the last one.
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            OllamaChatResponse? chunk;
            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line);
            }
            catch (JsonException)
            {
                continue; // skip any malformed/partial line rather than aborting the whole stream
            }

            if (!string.IsNullOrEmpty(chunk?.Message?.Content))
            {
                yield return chunk.Message.Content;
            }

            if (chunk?.Done == true) yield break;
        }
    }

    private OllamaChatRequest BuildRequest(string systemPrompt, string userPrompt, bool stream) => new()
    {
        Model = _model,
        Stream = stream,
        Messages = new[]
        {
            new OllamaChatMessage { Role = "system", Content = systemPrompt },
            new OllamaChatMessage { Role = "user", Content = userPrompt },
        },
    };

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("stream")] public bool Stream { get; set; }
        [JsonPropertyName("messages")] public OllamaChatMessage[] Messages { get; set; } = Array.Empty<OllamaChatMessage>();
    }

    private sealed class OllamaChatMessage
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "";
        [JsonPropertyName("content")] public string Content { get; set; } = "";
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")] public OllamaChatMessage? Message { get; set; }
        [JsonPropertyName("done")] public bool Done { get; set; }
    }
}
