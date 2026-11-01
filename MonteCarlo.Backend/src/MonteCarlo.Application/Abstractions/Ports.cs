using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;
using MonteCarlo.Domain.Simulation;

namespace MonteCarlo.Application.Abstractions;

/// <summary>Persistence port. Infrastructure implements it with EF Core; use cases never see EF.</summary>
public interface IKnowledgeStore
{
    IQueryable<Project> Projects { get; }
    IQueryable<Person> People { get; }
    IQueryable<WorkItem> WorkItems { get; }
    IQueryable<WorkItemDependency> Dependencies { get; }
    IQueryable<Meeting> Meetings { get; }
    IQueryable<SpeakerTurn> SpeakerTurns { get; }
    IQueryable<Evidence> Evidence { get; }
    IQueryable<Decision> Decisions { get; }
    IQueryable<ActionItem> ActionItems { get; }
    IQueryable<Blocker> Blockers { get; }
    IQueryable<Suggestion> Suggestions { get; }
    IQueryable<CodeEvent> CodeEvents { get; }
    IQueryable<ExtractionLog> ExtractionLogs { get; }
    IQueryable<SimulationRun> SimulationRuns { get; }
    IQueryable<AuditEntry> AuditEntries { get; }
    IQueryable<TenantSettings> Settings { get; }

    void Add<T>(T entity) where T : class;
    void Remove<T>(T entity) where T : class;
    Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken ct = default);
    Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken ct = default);
    Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

// ---- Transcripts (Microsoft Graph or upload) ------------------------------------------------------

public sealed record Transcript(string MeetingId, string TranscriptId, string Title, DateTimeOffset StartedAt, string Content);

public interface ITranscriptSource
{
    bool IsConfigured { get; }
    Task<Transcript> GetAsync(string meetingId, string transcriptId, CancellationToken ct);
}

// ---- AI extraction ------------------------------------------------------------------------------

public sealed record KnownTicket(string Ref, string Title);

public sealed record ExtractionContext(string ProjectKey, DateOnly MeetingDate, IReadOnlyList<string> KnownPeople, IReadOnlyList<KnownTicket> OpenTickets);

public sealed record ExtractedDecision(string Statement, string? DecidedBy, string? Rationale, string? TicketRef, string Quote, int TurnSequence, double Confidence);

public sealed record ExtractedAction(string Description, string? Owner, DateOnly? DueDate, string? TicketRef, string Quote, int TurnSequence, double Confidence);

public sealed record ExtractedBlocker(string Description, string? RaisedBy, string? TicketRef, string Quote, int TurnSequence, double Confidence);

public sealed record ExtractionResult(
    IReadOnlyList<ExtractedDecision> Decisions,
    IReadOnlyList<ExtractedAction> Actions,
    IReadOnlyList<ExtractedBlocker> Blockers,
    string Model,
    string PromptVersion);

public interface ILlmExtractor
{
    string Name { get; }
    Task<ExtractionResult> ExtractAsync(IReadOnlyList<SpeakerTurn> turns, ExtractionContext ctx, CancellationToken ct);
}

// ---- Ticket system (Local, Jira, Azure DevOps ...) -------------------------------------------------

public sealed record ExternalTicket(
    string ExternalRef, string Title, string Description, WorkItemStatus Status,
    string? AssigneeEmail, double? EstimateDays, string? Release, DateTimeOffset UpdatedAt);

public sealed record WorkItemDraft(
    string ProjectKey, string Title, string Description, WorkRole Role,
    string? AssigneeEmail, DateOnly? DueDate, string Release, double? EstimateDays);

public sealed record TicketFieldUpdate(string? Release = null, string? AssigneeEmail = null, double? EstimateDays = null);

public interface ITicketSystem
{
    string Name { get; }
    /// <summary>False for the built-in local board (the database is the source of truth).</summary>
    bool IsExternal { get; }
    Task<IReadOnlyList<ExternalTicket>> GetOpenItemsAsync(string projectKey, CancellationToken ct);
    Task<string> CreateAsync(WorkItemDraft draft, CancellationToken ct);
    Task UpdateStatusAsync(string externalId, WorkItemStatus status, CancellationToken ct);
    Task UpdateFieldsAsync(string externalId, TicketFieldUpdate update, CancellationToken ct);
}

// ---- Code activity (Git / CI) -------------------------------------------------------------------

public sealed record CodeEventData(CodeEventType Type, string ExternalId, string Title, string Author, string Branch, DateTimeOffset OccurredAt, string Url);

public interface ICodeActivitySource
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<CodeEventData>> GetSinceAsync(DateTimeOffset since, CancellationToken ct);
}

// ---- Narration (AI explanation of simulation results) ---------------------------------------------

public sealed record MitigationResult(string Description, int RecoveredDays, int LostDays, DateOnly P80, double ProbabilityOnTime);

public sealed record NarrativeInput(string ScenarioName, IReadOnlyList<string> Changes, SimulationComparison Comparison, MitigationResult? Mitigation);

public interface IInsightNarrator
{
    Task<string> ExplainAsync(NarrativeInput input, CancellationToken ct);
}

// ---- Queue between webhook and extraction ----------------------------------------------------------

public sealed record IngestionJob(string Kind, string? ExternalMeetingId = null, string? TranscriptId = null, Guid? MeetingId = null);

public interface IIngestionQueue
{
    ValueTask EnqueueAsync(IngestionJob job, CancellationToken ct = default);
}
