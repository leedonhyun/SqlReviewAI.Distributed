namespace SqlReviewAI.Contracts;

// These mirror SqlReviewAI.Core.Models but live in Contracts, decorated for
// Orleans' code-generated serializer, so SqlReviewAI.Core can stay
// dependency-free while these DTOs cross the Silo <-> Web grain-call
// boundary. Mapping to/from the Core types happens in the grain
// implementations (SqlReviewAI.Grains), never in Core itself.

[GenerateSerializer]
public sealed record SqlFeaturesDto
{
    [Id(0)] public string StatementType { get; init; } = "UNKNOWN";
    [Id(1)] public string? PrimaryTable { get; init; }
    [Id(2)] public bool HasWhereClause { get; init; }
    [Id(3)] public IReadOnlyList<string> WhereColumns { get; init; } = Array.Empty<string>();
    [Id(4)] public bool SelectsAllColumns { get; init; }
    [Id(5)] public bool HasNoLockHint { get; init; }
    [Id(6)] public IReadOnlyList<string> JoinTypes { get; init; } = Array.Empty<string>();
    [Id(7)] public IReadOnlyList<string> UpdatedColumns { get; init; } = Array.Empty<string>();
    [Id(8)] public string RawSql { get; init; } = string.Empty;
    [Id(9)] public string NormalizedSql { get; init; } = string.Empty;
    [Id(10)] public string? SourceFile { get; init; }
}

public enum SeverityDto { Info = 1, Low = 2, Medium = 3, High = 4, Critical = 5 }

[GenerateSerializer]
public sealed record RuleFindingDto(
    [property: Id(0)] string RuleCode,
    [property: Id(1)] SeverityDto Severity,
    [property: Id(2)] string Title,
    [property: Id(3)] string Detail,
    [property: Id(4)] string? Evidence,
    [property: Id(5)] int SampleSize
);

[GenerateSerializer]
public sealed record SimilarExampleDto(
    [property: Id(0)] string SourceFile,
    [property: Id(1)] double SimilarityScore,
    [property: Id(2)] string Sql
);

public enum RiskLevelDto { Low, Medium, High, Critical }

[GenerateSerializer]
public sealed record ReviewResultDto(
    [property: Id(0)] SqlFeaturesDto Features,
    [property: Id(1)] int Score,
    [property: Id(2)] RiskLevelDto RiskLevel,
    [property: Id(3)] IReadOnlyList<RuleFindingDto> Findings,
    [property: Id(4)] string Explanation,
    [property: Id(5)] IReadOnlyList<SimilarExampleDto> SimilarExamples
);

[GenerateSerializer]
public sealed record TableStatementStatsDto(
    [property: Id(0)] string Table,
    [property: Id(1)] string StatementType,
    [property: Id(2)] int TotalCount,
    [property: Id(3)] int WithWhereCount,
    [property: Id(4)] int SelectStarCount,
    [property: Id(5)] int NoLockCount
);

[GenerateSerializer]
public sealed record DeletePatternStatsDto(
    [property: Id(0)] string Table,
    [property: Id(1)] int SoftDeleteUpdateCount,
    [property: Id(2)] int HardDeleteCount
);

[GenerateSerializer]
public sealed record CorpusStatisticsDto(
    [property: Id(0)] int TotalStatements,
    [property: Id(1)] IReadOnlyDictionary<string, TableStatementStatsDto> ByTableAndStatement,
    [property: Id(2)] IReadOnlyDictionary<string, int> JoinTypeCounts,
    [property: Id(3)] IReadOnlyDictionary<string, DeletePatternStatsDto> DeletePatternByTable,
    [property: Id(4)] int SelectStatementCount,
    [property: Id(5)] int SelectStarCount,
    [property: Id(6)] int SelectWithNoLockCount
);

/// <summary>
/// One event in the streaming review pipeline — mirrors the four logical
/// channels in the architecture diagram (rule engine / RAG / LLM / logs).
/// Delivered to callers either as an <c>IAsyncEnumerable&lt;ReviewProgressEvent&gt;</c>
/// grain-call stream, over a SignalR streaming hub method, or over one of
/// the four Nerdbank.Streams multiplexed channels — same event shape
/// regardless of transport.
/// </summary>
[GenerateSerializer]
public enum ReviewChannel { Rules, Rag, Llm, Logs }

[GenerateSerializer]
public sealed record ReviewProgressEvent(
    [property: Id(0)] ReviewChannel Channel,
    [property: Id(1)] string Kind, // e.g. "finding", "similar", "token", "info"
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] DateTimeOffset Timestamp
);
