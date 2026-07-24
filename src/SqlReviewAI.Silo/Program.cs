// SqlReviewAI.Silo — the Orleans cluster process. Run this first, then
// SqlReviewAI.Web (which connects to it as an Orleans client).
//
// Dev/local clustering only (UseLocalhostClustering). For a real multi-node
// deployment, swap in a real clustering provider (Azure Table, ADO.NET,
// Redis, Kubernetes, ...) — see
// https://learn.microsoft.com/dotnet/orleans/host/configuration-guide/cluster-management

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Orleans.Serialization.Configuration;
using Orleans;
using Orleans.Configuration;

using SqlReviewAI.Contracts;
using SqlReviewAI.Core.Abstractions;
using SqlReviewAI.Core.Embeddings;
using SqlReviewAI.Core.Extraction;
using SqlReviewAI.Core.Llm;

var ollamaUrl = Environment.GetEnvironmentVariable("Ollama:Url")?? "http://localhost:11434";
var chatModel = Environment.GetEnvironmentVariable("Ollama:ChatModel") ?? "qwen3:14b";
var embeddingModel = Environment.GetEnvironmentVariable("Ollama:EmbeddingModel") ?? "nomic-embed-text";

var host = Host.CreateDefaultBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()//siloPort: 11111, gatewayPort: 30000)
             //.AddMemoryGrainStorageAsDefault()
           // .Configure<Orleans.Configuration.TypeManifestOptions>(o => o.AllowAssemblies(typeof(ICorpusStatsGrain).Assembly))
            .Configure<ClusterOptions>(o =>
            {
                o.ClusterId = "sqlreview-dev";
                o.ServiceId = "SqlReviewAI";
            })
    .ConfigureLogging(logging =>
                    logging.AddConsole().SetMinimumLevel(LogLevel.Information)
                    )
            .ConfigureServices(services =>
            {
                // Stateless/shared -> Singleton is fine.
                services.AddSingleton<ISqlFeatureExtractor, RegexSqlFeatureExtractor>();
         //       services.addg(typeof(ICorpusStatsGrain));
                // Per-grain-activation state -> Transient, so every RagGrain
                // activation (one per corpus id) gets its own isolated index
                // instead of all corpora sharing one vector store.
                services.AddTransient<IVectorStore, InMemoryVectorStore>();

                if (ollamaUrl is not null)
                {
                    services.AddSingleton(new HttpClient { BaseAddress = new Uri(ollamaUrl), Timeout = TimeSpan.FromMinutes(5) });
                    services.AddSingleton<IEmbeddingProvider>(sp => new OllamaEmbeddingProvider(sp.GetRequiredService<HttpClient>(), embeddingModel));
                    services.AddSingleton(sp => new OllamaChatProvider(sp.GetRequiredService<HttpClient>(), chatModel));
                    services.AddSingleton<IChatLlmProvider>(sp => sp.GetRequiredService<OllamaChatProvider>());
                    services.AddSingleton<IStreamingChatLlmProvider>(sp => sp.GetRequiredService<OllamaChatProvider>());
                }
                else
                {
                    services.AddSingleton<IEmbeddingProvider, HashingBagOfWordsEmbeddingProvider>();
                    // No IChatLlmProvider registered -> LlmGrain falls back
                    // to TemplateExplanationBuilder automatically.
                }


            });

        siloBuilder.UseLocalhostClustering();
        siloBuilder.AddMemoryGrainStorage("store");
   //     siloBuilder.UseInMemoryReminderService();
    }).UseConsoleLifetime()

    .Build();

Console.WriteLine(ollamaUrl is not null
    ? $"[SqlReviewAI.Silo] Ollama 연동 활성화: {ollamaUrl} (chat={chatModel}, embed={embeddingModel})"
    : "[SqlReviewAI.Silo] Ollama 미설정 — 오프라인 모드(해싱 임베딩 + 템플릿 설명)로 실행합니다.");
Console.WriteLine("[SqlReviewAI.Silo] Gateway: localhost:30000, Silo: localhost:11111");

await host.RunAsync();
