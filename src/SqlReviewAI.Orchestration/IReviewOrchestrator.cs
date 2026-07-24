using SqlReviewAI.Core.Models;

namespace SqlReviewAI.Orchestration;

/// <summary>
/// Everything the SignalR hub / HTTP API needs to run a review. Two
/// implementations exist:
///   - InProcessReviewOrchestrator (this project) — runs the whole
///     pipeline in the Web process itself, no Orleans cluster needed.
///     This is the default, and what's actually build/run/tested here.
///   - SqlReviewAI.Web.OrleansIntegration.OrleansReviewOrchestrator —
///     delegates to an ISqlReviewGrain over an Orleans cluster client,
///     matching the architecture diagram's Silo boundary. See that
///     project's README section for wiring it in.
/// </summary>
public interface IReviewOrchestrator
{
    Task IngestCorpusAsync(string corpusId, IReadOnlyList<(string SourceFile, string Sql)> entries, CancellationToken ct);

    Task<ReviewResult> ReviewAsync(string corpusId, string sql, CancellationToken ct);

    IAsyncEnumerable<ReviewProgressEvent> ReviewStreamAsync(string corpusId, string sql, CancellationToken ct);

    Task<string> AskAsync(string corpusId, string sql, string question, CancellationToken ct);

    Task<int> GetCorpusSizeAsync(string corpusId, CancellationToken ct);
}
