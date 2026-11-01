using MonteCarlo.Application.Abstractions;
using MonteCarlo.Application.Common;
using MonteCarlo.Application.Dtos;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Rules;

namespace MonteCarlo.Application.Services;

/// <summary>Use case: ReconcileTickets. Talk vs tickets vs code; differences become suggestions.</summary>
public sealed class ReconciliationService(IKnowledgeStore store)
{
    public async Task<ReconcileResultDto> RunAsync(Guid? projectId, CancellationToken ct = default)
    {
        var project = await store.ResolveProjectAsync(projectId, ct);
        var settings = await store.GetSettingsAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-60);

        var snapshot = new ReconciliationSnapshot(
            project.Id, now, project.CurrentRelease, settings.StaleAfterDays,
            await store.ListAsync(store.WorkItems.Where(w => w.ProjectId == project.Id), ct),
            await store.ListAsync(store.People, ct),
            await store.ListAsync(store.ActionItems.Where(a => a.ProjectId == project.Id && a.State != ActionItemState.Rejected && a.State != ActionItemState.Done), ct),
            await store.ListAsync(store.Decisions.Where(d => d.ProjectId == project.Id && d.DecidedAt >= since), ct),
            await store.ListAsync(store.Blockers.Where(b => b.ProjectId == project.Id && b.ResolvedAt == null), ct),
            await store.ListAsync(store.CodeEvents.Where(c => c.ProjectId == project.Id && c.OccurredAt >= since), ct),
            (await store.ListAsync(store.Suggestions.Where(s => s.ProjectId == project.Id).Select(s => s.Fingerprint), ct)).ToHashSet());

        var created = ReconciliationPolicy.Evaluate(snapshot);
        foreach (var s in created) store.Add(s);
        if (created.Count > 0) store.Audit("system", "Reconcile", "Project", project.Id, $"{created.Count} new suggestions");
        await store.SaveChangesAsync(ct);

        var lookups = await Lookups.LoadAsync(store, project.Id, created.Select(c => c.EvidenceId), ct);
        return new ReconcileResultDto(created.Count, created.Select(lookups.ToDto).ToList());
    }
}
