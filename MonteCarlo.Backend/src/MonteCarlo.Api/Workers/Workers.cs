using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Services;
using MonteCarlo.Infrastructure.Integrations;
using MonteCarlo.Infrastructure.Queue;

namespace MonteCarlo.Api.Workers;

/// <summary>Processes queued transcripts (Graph notifications) in the background.</summary>
public sealed class IngestionWorker(ChannelIngestionQueue queue, IServiceScopeFactory scopes, ILogger<IngestionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var meetings = scope.ServiceProvider.GetRequiredService<MeetingService>();
                switch (job.Kind)
                {
                    case "TeamsTranscript" when job.ExternalMeetingId is not null && job.TranscriptId is not null:
                        var id = await meetings.IngestFromTeamsAsync(job.ExternalMeetingId, job.TranscriptId, stoppingToken);
                        log.LogInformation("Ingested Teams transcript {Transcript} as meeting {Meeting}", job.TranscriptId, id);
                        break;
                    case "Extract" when job.MeetingId is { } mid:
                        await meetings.ExtractAsync(mid, stoppingToken);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Ingestion job {Kind} failed", job.Kind);
            }
        }
    }
}

/// <summary>
/// Scheduled jobs: renew the Graph subscription, pull code activity, sync tickets, nightly reconciliation
/// and retention clean-up of raw transcripts.
/// </summary>
public sealed class MaintenanceWorker(IServiceScopeFactory scopes, GraphTranscriptSource graph, IConfiguration config, ILogger<MaintenanceWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, config.GetValue("Maintenance:IntervalMinutes", 60)));
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        using var timer = new PeriodicTimer(interval);
        do
        {
            await RunOnceAsync(stoppingToken);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await Safe("Graph subscription", () => graph.EnsureSubscriptionAsync(ct));
        using var scope = scopes.CreateScope();
        var sp = scope.ServiceProvider;
        var store = sp.GetRequiredService<IKnowledgeStore>();
        var projects = await store.ListAsync(store.Projects.Select(p => p.Id), ct);
        foreach (var pid in projects)
        {
            await Safe("Ticket sync", () => sp.GetRequiredService<WorkItemService>().SyncAsync(pid, ct));
            await Safe("Code pull", () => sp.GetRequiredService<CodeActivityService>().PullAsync(pid, ct));
            await Safe("Reconcile", () => sp.GetRequiredService<ReconciliationService>().RunAsync(pid, ct));
        }
        await Safe("Retention", () => sp.GetRequiredService<SettingsService>().PurgeRawTranscriptsAsync(ct));
    }

    private async Task Safe(string name, Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "{Job} failed", name); }
    }
}
