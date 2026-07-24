using System.Text.Json;
using Orleans;
using SqlReviewAI.Contracts;
using SqlReviewAI.Core.Abstractions;
using SqlReviewAI.Core.Models;
using SqlReviewAI.Core.Review;

namespace SqlReviewAI.Grains;

/// <summary>
/// SQL 리뷰 오케스트레이션. Keyed by corpus id — calls
/// CorpusStatsGrain(same key), RagGrain(same key), RuleEngineGrain(shared,
/// key 0) and LlmGrain(shared, key 0), then aggregates. This grain holds no
/// state of its own; all state lives in CorpusStatsGrain / RagGrain.
/// </summary>
public sealed class SqlReviewGrain : Grain, ISqlReviewGrain
{
    private const double DuplicateSimilarityThreshold = 0.98;

    private readonly ISqlFeatureExtractor _extractor;

    public SqlReviewGrain(ISqlFeatureExtractor extractor)
    {
        _extractor = extractor;
    }

    private string CorpusId => this.GetPrimaryKeyString();

    public async Task<ReviewResultDto> ReviewAsync(string sql)
    {
        var features = _extractor.Extract(sql);
        var featuresDto = features.ToDto();

        var stats = await GrainFactory.GetGrain<ICorpusStatsGrain>(CorpusId).GetStatisticsAsync();
        var findings = (await GrainFactory.GetGrain<IRuleEngineGrain>(0).EvaluateAsync(featuresDto, stats)).ToList();

        var similar = await GrainFactory.GetGrain<IRagGrain>(CorpusId).SearchAsync(featuresDto, topK: 3);
        AppendDuplicateFindingIfAny(similar, findings);

        var coreFindings = findings.Select(f => f.ToCore()).ToList();
        var score = ScoreCalculator.ComputeScore(coreFindings);
        var risk = ScoreCalculator.ToRiskLevel(score, coreFindings);

        var explanation = await GrainFactory.GetGrain<ILlmGrain>(0).ExplainAsync(featuresDto, findings, similar);

        return new ReviewResultDto(featuresDto, score, risk.ToDto(), findings, explanation, similar);
    }

    public async IAsyncEnumerable<ReviewProgressEvent> ReviewStreamAsync(string sql)
    {
        var features = _extractor.Extract(sql);
        var featuresDto = features.ToDto();

        yield return Log("리뷰 시작: 통계/규칙 검사 준비 중");

        var stats = await GrainFactory.GetGrain<ICorpusStatsGrain>(CorpusId).GetStatisticsAsync();
        var findings = (await GrainFactory.GetGrain<IRuleEngineGrain>(0).EvaluateAsync(featuresDto, stats)).ToList();

        foreach (var f in findings)
        {
            yield return new ReviewProgressEvent(ReviewChannel.Rules, "finding", JsonSerializer.Serialize(f), DateTimeOffset.UtcNow);
        }
        yield return Log($"규칙 검사 완료: {findings.Count}건 발견");

        var similar = await GrainFactory.GetGrain<IRagGrain>(CorpusId).SearchAsync(featuresDto, topK: 3);
        AppendDuplicateFindingIfAny(similar, findings);

        foreach (var s in similar)
        {
            yield return new ReviewProgressEvent(ReviewChannel.Rag, "similar", JsonSerializer.Serialize(s), DateTimeOffset.UtcNow);
        }
        yield return Log($"RAG 유사도 검색 완료: {similar.Count}건");

        yield return Log("LLM 설명 생성 중");
        await foreach (var token in GrainFactory.GetGrain<ILlmGrain>(0).ExplainStreamAsync(featuresDto, findings, similar))
        {
            yield return new ReviewProgressEvent(ReviewChannel.Llm, "token", token, DateTimeOffset.UtcNow);
        }
        yield return Log("리뷰 완료");
    }

    public async Task<string> AskAsync(string sql, string question)
    {
        var features = _extractor.Extract(sql);
        var featuresDto = features.ToDto();

        var stats = await GrainFactory.GetGrain<ICorpusStatsGrain>(CorpusId).GetStatisticsAsync();
        var findings = await GrainFactory.GetGrain<IRuleEngineGrain>(0).EvaluateAsync(featuresDto, stats);
        var similar = await GrainFactory.GetGrain<IRagGrain>(CorpusId).SearchAsync(featuresDto, topK: 3);

        return await GrainFactory.GetGrain<ILlmGrain>(0).AskAsync(featuresDto, findings, similar, question);
    }

    private static void AppendDuplicateFindingIfAny(IReadOnlyList<SimilarExampleDto> similar, List<RuleFindingDto> findings)
    {
        var duplicate = similar.FirstOrDefault(s => s.SimilarityScore >= DuplicateSimilarityThreshold);
        if (duplicate is null) return;

        findings.Add(new RuleFindingDto(
            "DUPLICATE_SQL",
            SeverityDto.Info,
            "동일/유사 SQL이 기존 시스템에 존재함",
            $"기존 SQL({duplicate.SourceFile})과 매우 유사합니다. 새로 작성하기보다 기존 쿼리를 재사용하는 것을 검토하세요.",
            $"유사도 {duplicate.SimilarityScore:P1}",
            1));
    }

    private static ReviewProgressEvent Log(string message) =>
        new(ReviewChannel.Logs, "info", JsonSerializer.Serialize(new { message }), DateTimeOffset.UtcNow);
}
