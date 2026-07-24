using Orleans;
using SqlReviewAI.Contracts;
using SqlReviewAI.Core.Abstractions;
using SqlReviewAI.Core.Models;
using SqlReviewAI.Core.Statistics;

namespace SqlReviewAI.Grains;

/// <summary>
/// 회사 SQL 통계/관례. One activation per corpus id (Orleans guarantees
/// single-activation-per-key, so ingests are naturally serialized — no
/// locking needed here). State is in-memory only for this reference
/// implementation; for production, replace the two private lists with
/// Orleans `[PersistentState]` fields backed by a storage provider so
/// statistics survive silo restarts.
/// </summary>
public sealed class CorpusStatsGrain : Grain, ICorpusStatsGrain 
{
    private readonly ISqlFeatureExtractor _extractor;
    private readonly List<SqlFeatures> _corpus = new();
    private CorpusStatistics _stats = new();

    public CorpusStatsGrain(ISqlFeatureExtractor extractor)
    {
        _extractor = extractor;
    }

    public Task IngestAsync(IReadOnlyList<RawSqlEntry> entries)
    {
        foreach (var entry in entries)
        {
            _corpus.Add(_extractor.Extract(entry.Sql, entry.SourceFile));
        }

        _stats = new SqlCorpusAnalyzer().Analyze(_corpus);
        return Task.CompletedTask;
    }

    public Task<CorpusStatisticsDto> GetStatisticsAsync() => Task.FromResult(_stats.ToDto());

    public Task<int> GetStatementCountAsync() => Task.FromResult(_corpus.Count);
}
