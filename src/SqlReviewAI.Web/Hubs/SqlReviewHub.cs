using Microsoft.AspNetCore.SignalR;
using SqlReviewAI.Orchestration;

namespace SqlReviewAI.Web.Hubs;

/// <summary>
/// Client-facing real-time surface. Clients call:
///   - Review(corpusId, sql)        -> single Task&lt;ReviewResult&gt; response
///   - ReviewStream(corpusId, sql)  -> IAsyncEnumerable stream: rule findings,
///                                     then RAG hits, then LLM tokens, then a
///                                     final log line — the four logical
///                                     channels from the architecture
///                                     diagram, delivered over one SignalR
///                                     connection.
///   - Ask(corpusId, sql, question) -> single Task&lt;string&gt; response
///
/// JS client example (see README):
///   const stream = connection.stream("ReviewStream", "default", sql);
///   stream.subscribe({ next: e => ..., complete: () => ..., error: err => ... });
/// </summary>
public sealed class SqlReviewHub : Hub
{
    private readonly IReviewOrchestrator _orchestrator;

    public SqlReviewHub(IReviewOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public Task<Core.Models.ReviewResult> Review(string corpusId, string sql) =>
        _orchestrator.ReviewAsync(string.IsNullOrWhiteSpace(corpusId) ? "default" : corpusId, sql, Context.ConnectionAborted);

    public IAsyncEnumerable<ReviewProgressEvent> ReviewStream(string corpusId, string sql, CancellationToken cancellationToken) =>
        _orchestrator.ReviewStreamAsync(string.IsNullOrWhiteSpace(corpusId) ? "default" : corpusId, sql, cancellationToken);

    public Task<string> Ask(string corpusId, string sql, string question) =>
        _orchestrator.AskAsync(string.IsNullOrWhiteSpace(corpusId) ? "default" : corpusId, sql, question, Context.ConnectionAborted);
}
