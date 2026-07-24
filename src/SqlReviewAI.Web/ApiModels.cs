namespace SqlReviewAI.Web;

public sealed record ReviewRequest(string Sql, string? CorpusId);

public sealed record AskRequest(string Sql, string Question, string? CorpusId);

public sealed record IngestEntry(string SourceFile, string Sql);

public sealed record IngestRequest(IReadOnlyList<IngestEntry> Entries);
