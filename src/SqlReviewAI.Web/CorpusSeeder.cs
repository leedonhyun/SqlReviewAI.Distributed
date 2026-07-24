using Microsoft.AspNetCore.Hosting;
using SqlReviewAI.Orchestration;

namespace SqlReviewAI.Web
{
    public class CorpusSeeder : IHostedService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<CorpusSeeder> _logger;
        private readonly IWebHostEnvironment _env;

        public CorpusSeeder(IServiceProvider sp, ILogger<CorpusSeeder> logger, IWebHostEnvironment env)
        {
            _sp = sp;
            _logger = logger;
            _env = env;
        }

        public async Task StartAsync(CancellationToken ct)
        {
            using var scope = _sp.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IReviewOrchestrator>();

            var corpusDir = Path.Combine(AppContext.BaseDirectory, "corpus");

            if (!Directory.Exists(corpusDir))
            {
                // fall back to the repo-root corpus/ folder when running via `dotnet run`
                corpusDir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "corpus"));
            }

            if (Directory.Exists(corpusDir))
            {
                var entries = Directory.GetFiles(corpusDir, "*.sql", SearchOption.AllDirectories)
                    .Select(f => (SourceFile: Path.GetFileName(f), Sql: File.ReadAllText(f)))
                    .Where(e => !string.IsNullOrWhiteSpace(e.Sql))
                    .ToList();

                if (entries.Count > 0)
                {
                    await orchestrator.IngestCorpusAsync("default", entries, ct);
                    _logger.LogInformation("Loaded {Count} historical SQL statements from {Dir} into corpus 'default'", entries.Count, corpusDir);
                }
            }
            else
            {
                _logger.LogWarning("No corpus directory found — POST /api/corpus/default/ingest to add historical SQL before reviewing.");
            }
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
