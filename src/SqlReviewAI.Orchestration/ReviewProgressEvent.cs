namespace SqlReviewAI.Orchestration;

/// <summary>
/// Web-layer mirror of SqlReviewAI.Contracts.ReviewProgressEvent, deliberately
/// duplicated (rather than referencing Contracts) so this project has zero
/// Orleans dependency and can run standalone with the in-process
/// orchestrator. SqlReviewAI.Web.OrleansIntegration maps Contracts' version
/// onto this one at the boundary.
/// </summary>
public enum ReviewChannel { Rules, Rag, Llm, Logs }

public sealed record ReviewProgressEvent(ReviewChannel Channel, string Kind, string PayloadJson, DateTimeOffset Timestamp);
