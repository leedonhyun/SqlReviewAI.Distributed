// SqlReviewAI.Web — the ASP.NET Core front door: SignalR Hub + HTTP API +
// Swagger/OpenAPI. Runs standalone (in-process pipeline, no Orleans needed)
// by default; see the README section "Orleans로 전환하기" to point it at a
// real Silo cluster instead.

using Microsoft.AspNetCore.SignalR;
using SqlReviewAI.Core.Abstractions;
using SqlReviewAI.Core.Embeddings;
using SqlReviewAI.Core.Extraction;
using SqlReviewAI.Core.Llm;
using SqlReviewAI.Web;
using SqlReviewAI.Web.Hubs;
using SqlReviewAI.Orchestration;
using SqlReviewAI.Web.OrleansIntegration;
var builder = WebApplication.CreateBuilder(args);

var ollamaUrl = builder.Configuration["Ollama:Url"] ?? Environment.GetEnvironmentVariable("OLLAMA_URL");
var chatModel = builder.Configuration["Ollama:ChatModel"] ?? "qwen3:14b";
var embeddingModel = builder.Configuration["Ollama:EmbeddingModel"] ?? "nomic-embed-text";

// ---- Core services -------------------------------------------------------
builder.Services.AddSingleton<ISqlFeatureExtractor, RegexSqlFeatureExtractor>();
builder.Services.AddHostedService<CorpusSeeder>();
if (ollamaUrl is not null)
{
    builder.Services.AddSingleton(new HttpClient { BaseAddress = new Uri(ollamaUrl) });
    builder.Services.AddSingleton(sp => new OllamaChatProvider(sp.GetRequiredService<HttpClient>(), chatModel));
    builder.Services.AddSingleton<IChatLlmProvider>(sp => sp.GetRequiredService<OllamaChatProvider>());
    builder.Services.AddSingleton<IStreamingChatLlmProvider>(sp => sp.GetRequiredService<OllamaChatProvider>());
    builder.Services.AddSingleton<Func<IEmbeddingProvider>>(sp =>
        () => new OllamaEmbeddingProvider(sp.GetRequiredService<HttpClient>(), embeddingModel));
}
else
{
    builder.Services.AddSingleton<Func<IEmbeddingProvider>>(_ => () => new HashingBagOfWordsEmbeddingProvider());
}

// Default orchestrator: everything runs in this process. Swap for
// SqlReviewAI.Web.OrleansIntegration.OrleansReviewOrchestrator to delegate
// to a real Orleans Silo cluster instead (see README).
//builder.Services.AddSingleton<IReviewOrchestrator, InProcessReviewOrchestrator>();
builder.Host.UseSqlReviewOrleansClient();// (client => client.UseLocalhostClustering());

builder.Services.AddOrleansReviewOrchestrator();

// ---- SignalR + HTTP API + OpenAPI/Swagger ---------------------------------
builder.Services.AddSignalR();
builder.Services.AddOpenApi();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .SetIsOriginAllowed(_ => true) // dev-friendly; tighten for production
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();


app.UseCors();
app.MapOpenApi(); // serves /openapi/v1.json
app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "SqlReviewAI v1")); // serves /swagger

app.MapHub<SqlReviewHub>("/hubs/sql-review");

app.MapPost("/api/review", async (ReviewRequest req, IReviewOrchestrator orchestrator, CancellationToken ct) =>
{
    var result = await orchestrator.ReviewAsync(req.CorpusId ?? "default", req.Sql, ct);
    return Results.Ok(result);
})
.WithName("ReviewSql")
.WithSummary("SQL 한 건을 검사하고 점수/위험도/발견사항/설명을 반환합니다.")
.WithOpenApi();

app.MapPost("/api/ask", async (AskRequest req, IReviewOrchestrator orchestrator, CancellationToken ct) =>
{
    var answer = await orchestrator.AskAsync(req.CorpusId ?? "default", req.Sql, req.Question, ct);
    return Results.Ok(new { answer });
})
.WithName("AskAboutSql")
.WithSummary("특정 SQL에 대해 자유 질의응답합니다 (Ollama 연동 필요).")
.WithOpenApi();

app.MapPost("/api/corpus/{corpusId}/ingest", async (string corpusId, IngestRequest req, IReviewOrchestrator orchestrator, CancellationToken ct) =>
{
    await orchestrator.IngestCorpusAsync(corpusId, req.Entries.Select(e => (e.SourceFile, e.Sql)).ToList(), ct);
    var count = await orchestrator.GetCorpusSizeAsync(corpusId, ct);
    return Results.Ok(new { corpusId, totalStatements = count });
})
.WithName("IngestCorpus")
.WithSummary("이력 SQL을 코퍼스에 추가합니다.")
.WithOpenApi();

app.MapGet("/api/corpus/{corpusId}/size", async (string corpusId, IReviewOrchestrator orchestrator, CancellationToken ct) =>
    Results.Ok(new { corpusId, totalStatements = await orchestrator.GetCorpusSizeAsync(corpusId, ct) }))
.WithName("GetCorpusSize")
.WithOpenApi();

app.MapGet("/", () => Results.Redirect("/swagger"));

// ---- Bootstrap: load ./corpus/*.sql into the "default" corpus, if present
//var corpusDir = Path.Combine(AppContext.BaseDirectory, "corpus");
//if (!Directory.Exists(corpusDir))
//{
//    // fall back to the repo-root corpus/ folder when running via `dotnet run`
//    corpusDir = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "corpus"));
//}

//if (Directory.Exists(corpusDir))
//{
//    var orchestrator = app.Services.GetRequiredService<IReviewOrchestrator>();
//    var entries = Directory.GetFiles(corpusDir, "*.sql", SearchOption.AllDirectories)
//        .Select(f => (SourceFile: Path.GetFileName(f), Sql: File.ReadAllText(f)))
//        .Where(e => !string.IsNullOrWhiteSpace(e.Sql))
//        .ToList();

//    if (entries.Count > 0)
//    {
//        await orchestrator.IngestCorpusAsync("default", entries, CancellationToken.None);
//        app.Logger.LogInformation("Loaded {Count} historical SQL statements from {Dir} into corpus 'default'", entries.Count, corpusDir);
//    }
//}
//else
//{
//    app.Logger.LogWarning("No corpus directory found — POST /api/corpus/default/ingest to add historical SQL before reviewing.");
//}

app.Logger.LogInformation(ollamaUrl is not null
    ? "Ollama 연동 활성화: {Url} (chat={Chat}, embed={Embed})"
    : "Ollama 미설정 — 오프라인 모드(해싱 임베딩 + 템플릿 설명)로 실행합니다.", ollamaUrl, chatModel, embeddingModel);

app.Run();
