using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using SqlReviewAI.Orchestration;

namespace SqlReviewAI.Web.OrleansIntegration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Wires up an Orleans cluster client pointed at the localhost-clustering
    /// Silo from SqlReviewAI.Silo, and registers <see cref="OrleansReviewOrchestrator"/>
    /// as the <see cref="IReviewOrchestrator"/> implementation.
    ///
    /// Usage in SqlReviewAI.Web's Program.cs, replacing the
    /// `builder.Services.AddSingleton&lt;IReviewOrchestrator, InProcessReviewOrchestrator&gt;()`
    /// line:
    /// <code>
    /// builder.Host.UseOrleansClient(client => client.UseLocalhostClustering());
    /// builder.Services.AddOrleansReviewOrchestrator();
    /// </code>
    /// (UseOrleansClient itself comes from Microsoft.Orleans.Client — call it
    /// on the host builder before AddOrleansReviewOrchestrator.)
    /// </summary>
    public static IServiceCollection AddOrleansReviewOrchestrator(this IServiceCollection services)
    {
        services.AddSingleton<IReviewOrchestrator, OrleansReviewOrchestrator>();
        return services;
    }

    /// <summary>Convenience wrapper around UseOrleansClient + localhost clustering,
    /// matching SqlReviewAI.Silo's UseLocalhostClustering(11111, 30000).</summary>
    public static IHostBuilder UseSqlReviewOrleansClient(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseOrleansClient(client =>
        {
        // .UseLocalhostClustering(siloPort: 11111, gatewayPort: 30000)

        client.UseLocalhostClustering()// gatewayPort: 30000)

                .Configure<ClusterOptions>(o =>
                {
                    o.ClusterId = "sqlreview-dev";
                    o.ServiceId = "SqlReviewAI";

                });
        });
    }
}
