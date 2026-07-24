using Orleans;
using SqlReviewAI.Contracts;
using SqlReviewAI.Core.Models;
using SqlReviewAI.Orchestration;

using ReviewChannel = SqlReviewAI.Orchestration.ReviewChannel;
using ReviewProgressEvent = SqlReviewAI.Orchestration.ReviewProgressEvent;

namespace SqlReviewAI.Web.OrleansIntegration;

/// <summary>
/// Delegates every operation to <see cref="ISqlReviewGrain"/> /
/// <see cref="ICorpusStatsGrain"/> over an Orleans <see cref="IClusterClient"/>,
/// matching the architecture diagram's Web-App-to-Silo boundary. Register
/// this instead of InProcessReviewOrchestrator once a Silo cluster is
/// reachable — see AddOrleansReviewOrchestrator below.
/// </summary>
public sealed class OrleansReviewOrchestrator : IReviewOrchestrator
{
    private readonly IClusterClient _client;

    public OrleansReviewOrchestrator(IClusterClient client)
    {
        _client = client;
    }

    public Task IngestCorpusAsync(string corpusId, IReadOnlyList<(string SourceFile, string Sql)> entries, CancellationToken ct)
    {
        var grain = _client.GetGrain<ICorpusStatsGrain>(corpusId);
        
        var dtoEntries = entries.Select(e => new RawSqlEntry(e.SourceFile, e.Sql)).ToList();
        return grain.IngestAsync(dtoEntries);
    }

    public async Task<int> GetCorpusSizeAsync(string corpusId, CancellationToken ct)
    {
        var grain = _client.GetGrain<ICorpusStatsGrain>(corpusId);
        return await grain.GetStatementCountAsync();
    }

    public async Task<ReviewResult> ReviewAsync(string corpusId, string sql, CancellationToken ct)
    {
        var grain = _client.GetGrain<ISqlReviewGrain>(corpusId);
        var dto = await grain.ReviewAsync(sql);
        return dto.ToCore();
    }

    public async IAsyncEnumerable<ReviewProgressEvent> ReviewStreamAsync(
        string corpusId, string sql, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var grain = _client.GetGrain<ISqlReviewGrain>(corpusId);
        await foreach (var evt in grain.ReviewStreamAsync(sql).WithCancellation(ct))
        {
            yield return evt.ToWeb();
        }
    }

    public Task<string> AskAsync(string corpusId, string sql, string question, CancellationToken ct)
    {
        var grain = _client.GetGrain<ISqlReviewGrain>(corpusId);
        return grain.AskAsync(sql, question);
    }
}

internal static class Mapping
{
    public static ReviewChannel ToWeb(this SqlReviewAI.Contracts.ReviewChannel c) => c switch
    {
        SqlReviewAI.Contracts.ReviewChannel.Rules => ReviewChannel.Rules,
        SqlReviewAI.Contracts.ReviewChannel.Rag => ReviewChannel.Rag,
        SqlReviewAI.Contracts.ReviewChannel.Llm => ReviewChannel.Llm,
        SqlReviewAI.Contracts.ReviewChannel.Logs => ReviewChannel.Logs,
        _ => throw new ArgumentOutOfRangeException(nameof(c)),
    };

    public static ReviewProgressEvent ToWeb(this Contracts.ReviewProgressEvent e) =>
        new(e.Channel.ToWeb(), e.Kind, e.PayloadJson, e.Timestamp);

    public static Severity ToCore(this SeverityDto s) => (Severity)(int)s;

    public static RuleFinding ToCore(this RuleFindingDto f) =>
        new(f.RuleCode, f.Severity.ToCore(), f.Title, f.Detail, f.Evidence, f.SampleSize);

    public static SimilarExample ToCore(this SimilarExampleDto s) =>
        new(s.SourceFile, s.SimilarityScore, s.Sql);

    public static RiskLevel ToCore(this RiskLevelDto r) => (RiskLevel)(int)r;

    public static SqlFeatures ToCore(this SqlFeaturesDto d) => new()
    {
        StatementType = d.StatementType,
        PrimaryTable = d.PrimaryTable,
        HasWhereClause = d.HasWhereClause,
        WhereColumns = d.WhereColumns,
        SelectsAllColumns = d.SelectsAllColumns,
        HasNoLockHint = d.HasNoLockHint,
        JoinTypes = d.JoinTypes,
        UpdatedColumns = d.UpdatedColumns,
        RawSql = d.RawSql,
        NormalizedSql = d.NormalizedSql,
        SourceFile = d.SourceFile,
    };

    public static ReviewResult ToCore(this ReviewResultDto d) => new(
        d.Features.ToCore(),
        d.Score,
        d.RiskLevel.ToCore(),
        d.Findings.Select(f => f.ToCore()).ToList(),
        d.Explanation,
        d.SimilarExamples.Select(s => s.ToCore()).ToList()
    );
}
