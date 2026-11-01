using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Simulation;

namespace MonteCarlo.Application.Dtos;

// ---- Projects & people ------------------------------------------------------------------------------

public sealed record ProjectDto(Guid Id, string Key, string Name, string Description, DateOnly StartDate, DateOnly Deadline, string CurrentRelease);

public sealed record UpsertProjectRequest(string Key, string Name, string? Description, DateOnly StartDate, DateOnly Deadline, string? CurrentRelease);

public sealed record PersonDto(Guid Id, string Name, string Email, string? ExternalUserId, WorkRole Role, string Skills, double Capacity,
    DateOnly StartDate, DateOnly? LeaveDate, int OpenItems, bool Active);

public sealed record UpsertPersonRequest(string Name, string? Email, string? ExternalUserId, WorkRole Role, string? Skills, double Capacity,
    DateOnly? StartDate, DateOnly? LeaveDate);

// ---- Work items --------------------------------------------------------------------------------------

public sealed record WorkItemDto(
    Guid Id, string ExternalRef, string Title, string Description, string AcceptanceCriteria, WorkItemStatus Status, WorkRole Role,
    double? EstimateDays, double? ActualDays, Guid? OwnerId, string? OwnerName, string Release, int Priority,
    DateTimeOffset LastActivityAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    IReadOnlyList<Guid> DependsOn, IReadOnlyList<string> DependsOnRefs,
    int QualityScore, IReadOnlyList<string> QualityIssues, bool IsStale, int PendingSuggestions);

public sealed record UpsertWorkItemRequest(
    Guid? ProjectId, string? ExternalRef, string Title, string? Description, string? AcceptanceCriteria, WorkItemStatus? Status, WorkRole Role,
    double? EstimateDays, Guid? OwnerId, string? Release, int? Priority, IReadOnlyList<Guid>? DependsOn);

public sealed record ChangeStatusRequest(WorkItemStatus Status, string? Actor);

public sealed record TimelineEntryDto(DateTimeOffset At, string Kind, string Text, string? Source, EvidenceDto? Evidence);

public sealed record WorkItemDetailDto(WorkItemDto Item, IReadOnlyList<TimelineEntryDto> Timeline);

// ---- Evidence & knowledge -------------------------------------------------------------------------

public sealed record EvidenceDto(Guid Id, SourceType SourceType, string SourceId, Guid? MeetingId, string? MeetingTitle, int? OffsetSeconds,
    string Timestamp, string SpeakerName, string QuotedText, double Confidence);

public sealed record DecisionDto(Guid Id, string Statement, string DecidedByName, DateTimeOffset DecidedAt, string Rationale,
    Guid? AffectedWorkItemId, string? AffectedWorkItemRef, Guid? MeetingId, EvidenceDto? Evidence);

public sealed record ActionItemDto(Guid Id, string Description, Guid? OwnerId, string OwnerName, DateOnly? DueDate,
    Guid? LinkedWorkItemId, string? LinkedWorkItemRef, ActionItemState State, Guid? MeetingId, EvidenceDto? Evidence);

public sealed record BlockerDto(Guid Id, string Description, Guid? BlockingWorkItemId, string? BlockingWorkItemRef, string RaisedByName,
    DateTimeOffset RaisedAt, DateTimeOffset? ResolvedAt, Guid? MeetingId, EvidenceDto? Evidence);

// ---- Meetings ---------------------------------------------------------------------------------------

public sealed record IngestTranscriptRequest(Guid? ProjectId, string Title, DateTimeOffset? StartedAt, string Content, bool OptOut = false);

public sealed record MeetingSummaryDto(Guid Id, string Title, DateTimeOffset StartedAt, MeetingSource Source, bool Processed, bool OptedOut,
    bool RawDeleted, int Turns, int Decisions, int Actions, int Blockers);

public sealed record SpeakerTurnDto(int Sequence, int OffsetSeconds, string Timestamp, string SpeakerName, Guid? PersonId, string Text);

public sealed record ExtractionLogDto(string Extractor, string Model, string PromptVersion, int InputChars, long DurationMs, DateTimeOffset At);

public sealed record MeetingDetailDto(MeetingSummaryDto Meeting, IReadOnlyList<SpeakerTurnDto> Turns, IReadOnlyList<DecisionDto> Decisions,
    IReadOnlyList<ActionItemDto> Actions, IReadOnlyList<BlockerDto> Blockers, IReadOnlyList<SuggestionDto> Suggestions,
    IReadOnlyList<ExtractionLogDto> Extractions);

// ---- Suggestions ------------------------------------------------------------------------------------

public sealed record SuggestionDto(
    Guid Id, SuggestionType Type, string Title, string Detail, double Confidence, SuggestionOutcome Outcome, DateTimeOffset CreatedAt,
    Guid? WorkItemId, string? WorkItemRef, string? WorkItemTitle, WorkItemStatus? CurrentStatus,
    string? ProposedTitle, WorkItemStatus? ProposedStatus, Guid? ProposedOwnerId, string? ProposedOwnerName, DateOnly? ProposedDueDate,
    string? ProposedRelease, double? ProposedEstimateDays, WorkRole? ProposedRole,
    string? ReviewedBy, DateTimeOffset? ReviewedAt, string? ReviewNote, EvidenceDto? Evidence);

public sealed record ReviewRequest(
    string? Reviewer, string? Note,
    string? ProposedTitle = null, WorkItemStatus? ProposedStatus = null, Guid? ProposedOwnerId = null,
    DateOnly? ProposedDueDate = null, string? ProposedRelease = null, double? ProposedEstimateDays = null, WorkRole? ProposedRole = null);

public sealed record ReconcileResultDto(int Created, IReadOnlyList<SuggestionDto> Suggestions);

// ---- Code activity ----------------------------------------------------------------------------------

public sealed record CodeEventRequest(Guid? ProjectId, CodeEventType Type, string? ExternalId, string Title, string? Author, string? Branch,
    DateTimeOffset? OccurredAt, string? Url);

public sealed record CodeEventDto(Guid Id, CodeEventType Type, string ExternalId, string Title, string Author, string Branch,
    IReadOnlyList<string> TicketRefs, DateTimeOffset OccurredAt, string Url);

// ---- Search -----------------------------------------------------------------------------------------

public sealed record SearchHitDto(string Kind, Guid Id, string Text, double Score, DateTimeOffset At, string? Who, string? WorkItemRef, EvidenceDto? Evidence);

public sealed record SearchResultDto(string Query, string Answer, IReadOnlyList<SearchHitDto> Hits);

// ---- Dashboard --------------------------------------------------------------------------------------

public sealed record StatusCountDto(WorkItemStatus Status, int Count);

public sealed record DashboardDto(
    ProjectDto Project,
    int OpenItems, int DoneItems, int BlockedItems, int PendingSuggestions,
    double TicketFreshness, double? SuggestionPrecision,
    int MeetingsProcessed, int DecisionsCaptured, int ActionItemsCaptured, int OpenBlockers,
    IReadOnlyList<StatusCountDto> StatusCounts,
    IReadOnlyList<WorkItemDto> StaleItems,
    IReadOnlyList<WorkItemDto> LowQualityItems,
    IReadOnlyList<DecisionDto> RecentDecisions,
    IReadOnlyList<BlockerDto> Blockers,
    SimulationRunSummaryDto? LatestForecast);

public sealed record DigestDto(DateOnly WeekOf, string Text, IReadOnlyList<WorkItemDto> Stale, IReadOnlyList<SuggestionDto> Pending, IReadOnlyList<DecisionDto> Decisions);

// ---- Simulation -------------------------------------------------------------------------------------

/// <summary>Flat scenario change used over HTTP. Type: PersonLeaves, NewHire, ScopeChange, RemoveTask, PrioritySwap, MeetingLoad, ClientDelay.</summary>
public sealed record ScenarioChangeDto(
    string Type,
    Guid? PersonId = null,
    Guid? TaskId = null,
    int? Day = null,
    double? KnowledgeLoss = null,
    string? Name = null,
    WorkRole? Role = null,
    double? Capacity = null,
    int? RampUpDays = null,
    int? AddTasks = null,
    double? EffortDaysEach = null,
    double? EffortMultiplier = null,
    int? NewPriority = null,
    double? CapacityDelta = null,
    int? UntilDay = null);

public sealed record RunSimulationRequest(
    Guid? ProjectId,
    string? Name,
    IReadOnlyList<ScenarioChangeDto>? Changes,
    int Iterations = 1000,
    int Seed = 42,
    double AbsenceRate = 0.03,
    bool AutoMitigate = true,
    bool IncludeAllReleases = false,
    DateOnly? Deadline = null);

public sealed record SimPersonDto(Guid Id, string Name, WorkRole Role, double Capacity, int StartDay, int? LeaveDay);

public sealed record SimTaskDto(Guid Id, string Key, string Title, WorkRole Role, double EstimateDays, double RemainingFraction,
    IReadOnlyList<Guid> DependsOn, int Priority, Guid? OwnerId, int BlockedUntilDay);

public sealed record CalibrationDto(double MeanRatio, double LogSigma, int Samples, IReadOnlyDictionary<string, RoleCalibration> ByRole);

public sealed record SimulationModelDto(Guid ProjectId, string ProjectName, DateOnly StartDate, DateOnly Deadline, string Release,
    IReadOnlyList<SimPersonDto> People, IReadOnlyList<SimTaskDto> Tasks, CalibrationDto Calibration);

public sealed record SimulationResultDto(
    Guid RunId, string Name, DateTimeOffset CreatedAt, int Iterations, int Seed,
    IReadOnlyList<ScenarioChangeDto> Changes, IReadOnlyList<string> ChangeDescriptions,
    SimulationComparison Comparison, MitigationResultDto? Mitigation, string Narrative, CalibrationDto Calibration);

public sealed record MitigationResultDto(string Description, int RecoveredDays, int LostDays, DateOnly P80, double ProbabilityOnTime);

public sealed record SimulationRunSummaryDto(Guid Id, string Name, DateTimeOffset CreatedAt, int Iterations,
    DateOnly BaselineP50, DateOnly BaselineP80, DateOnly BaselineP95, DateOnly ScenarioP50, DateOnly ScenarioP80, DateOnly ScenarioP95,
    double BaselineOnTime, double ScenarioOnTime, string Narrative);

// ---- Settings & audit -------------------------------------------------------------------------------

public sealed record SettingsDto(string TeamName, int RetentionDays, bool CaptureEnabledByDefault, string DataRegion, int StaleAfterDays,
    double MinConfidence, string Extractor, string TicketSystem, string CodeSource, bool GraphConfigured);

public sealed record UpdateSettingsRequest(string? TeamName, int? RetentionDays, bool? CaptureEnabledByDefault, string? DataRegion,
    int? StaleAfterDays, double? MinConfidence);

public sealed record AuditEntryDto(DateTimeOffset At, string Actor, string Action, string EntityType, string EntityId, string Details);

// ---- 3D lab: risk landscape -------------------------------------------------------

/// <summary>Axes: Capacity (team focus), Scope (effort multiplier), Hires (extra people in the bottleneck role), Absence (daily absence rate).</summary>
public sealed record LandscapeRequest(
    Guid? ProjectId,
    string XAxis = "Capacity",
    string ZAxis = "Scope",
    int Steps = 7,
    int Iterations = 300,
    int Seed = 42,
    double AbsenceRate = 0.03,
    bool IncludeAllReleases = false);

public sealed record LandscapeAxisDto(string Name, string Label, IReadOnlyList<double> Values, IReadOnlyList<string> Labels, int BaselineIndex);

public sealed record LandscapeCellDto(int Xi, int Zi, int P50Days, int P80Days, int P95Days, DateOnly P80, double ProbabilityOnTime);

public sealed record LandscapeResultDto(
    DateOnly StartDate, DateOnly? Deadline, int? DeadlineDay, string HireRole, int Iterations,
    LandscapeAxisDto X, LandscapeAxisDto Z, IReadOnlyList<LandscapeCellDto> Cells, long ElapsedMs);

public sealed record MonteCarloTraceRequest(
    Guid? ProjectId,
    IReadOnlyList<ScenarioChangeDto>? Changes,
    int Iterations = 1000,
    int PathCount = 200,
    int Seed = 42,
    double AbsenceRate = 0.03,
    bool IncludeAllReleases = false);

public sealed record MonteCarloTraceDto(string Name, IReadOnlyList<string> ChangeDescriptions, long ElapsedMs, MonteCarloTrace Trace);
