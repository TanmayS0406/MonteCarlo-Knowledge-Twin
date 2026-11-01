using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Infrastructure.Extraction;
using MonteCarlo.Infrastructure.Integrations;
using MonteCarlo.Infrastructure.Persistence;
using MonteCarlo.Infrastructure.Queue;

namespace MonteCarlo.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var cs = config.GetConnectionString("Default")
                 ?? "Server=(localdb)\\MSSQLLocalDB;Database=MonteCarloTwin;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(3)));
        services.AddScoped<IKnowledgeStore>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<LlmOptions>(config.GetSection("Llm"));
        services.Configure<JiraOptions>(config.GetSection("Jira"));
        services.Configure<GitHubOptions>(config.GetSection("GitHub"));
        services.Configure<GraphOptions>(config.GetSection("Graph"));

        // AI extraction + narration
        services.AddHttpClient<LlmClient>(c => c.Timeout = TimeSpan.FromSeconds(120));
        services.AddSingleton<RuleBasedExtractor>();
        services.AddScoped<ILlmExtractor, LlmExtractor>();
        services.AddScoped<IInsightNarrator, Narrator>();

        // Ticket system: Jira when configured, otherwise the local board.
        services.AddHttpClient<JiraTicketSystem>();
        services.AddScoped<LocalTicketSystem>();
        services.AddScoped<ITicketSystem>(sp =>
            sp.GetRequiredService<IOptions<JiraOptions>>().Value.IsConfigured
                ? sp.GetRequiredService<JiraTicketSystem>()
                : sp.GetRequiredService<LocalTicketSystem>());

        // Code activity: GitHub when configured.
        services.AddHttpClient<GitHubCodeActivitySource>();
        services.AddScoped<ICodeActivitySource>(sp =>
            sp.GetRequiredService<IOptions<GitHubOptions>>().Value.IsConfigured
                ? sp.GetRequiredService<GitHubCodeActivitySource>()
                : new NullCodeActivitySource());

        // Teams transcripts via Microsoft Graph (singleton: caches token + subscription).
        services.AddHttpClient("graph");
        services.AddSingleton(sp => new GraphTranscriptSource(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient("graph"),
            sp.GetRequiredService<IOptions<GraphOptions>>(),
            sp.GetRequiredService<ILogger<GraphTranscriptSource>>()));
        services.AddSingleton<ITranscriptSource>(sp => sp.GetRequiredService<GraphTranscriptSource>());

        services.AddSingleton<ChannelIngestionQueue>();
        services.AddSingleton<IIngestionQueue>(sp => sp.GetRequiredService<ChannelIngestionQueue>());

        services.AddScoped<DataSeeder>();
        return services;
    }
}
