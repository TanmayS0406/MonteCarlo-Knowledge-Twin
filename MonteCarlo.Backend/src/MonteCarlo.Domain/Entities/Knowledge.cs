using MonteCarlo.Domain.Common;

namespace MonteCarlo.Domain.Entities;

/// <summary>Where a fact came from: the exact line, message or commit.</summary>
public class Evidence : Entity
{
    public SourceType SourceType { get; set; }
    public string SourceId { get; set; } = "";
    public Guid? MeetingId { get; set; }
    public int? OffsetSeconds { get; set; }
    public string SpeakerName { get; set; } = "";
    public string QuotedText { get; set; } = "";
    public double Confidence { get; set; }
}

public class Decision : Entity
{
    public Guid ProjectId { get; set; }
    public Guid? MeetingId { get; set; }
    public string Statement { get; set; } = "";
    public Guid? DecidedById { get; set; }
    public string DecidedByName { get; set; } = "";
    public DateTimeOffset DecidedAt { get; set; }
    public string Rationale { get; set; } = "";
    public Guid? AffectedWorkItemId { get; set; }
    public Guid? EvidenceId { get; set; }
}

public class ActionItem : Entity
{
    public Guid ProjectId { get; set; }
    public Guid? MeetingId { get; set; }
    public string Description { get; set; } = "";
    public Guid? OwnerId { get; set; }
    public string OwnerName { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public Guid? LinkedWorkItemId { get; set; }
    public ActionItemState State { get; set; } = ActionItemState.Proposed;
    public Guid? EvidenceId { get; set; }
}

public class Blocker : Entity
{
    public Guid ProjectId { get; set; }
    public Guid? MeetingId { get; set; }
    public string Description { get; set; } = "";
    public Guid? BlockingWorkItemId { get; set; }
    public Guid? RaisedById { get; set; }
    public string RaisedByName { get; set; } = "";
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public Guid? EvidenceId { get; set; }
}

/// <summary>A proposed change to a ticket. Never applied without a human.</summary>
public class Suggestion : Entity
{
    public Guid ProjectId { get; set; }
    public SuggestionType Type { get; set; }
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    /// <summary>Stable key so the same suggestion is never proposed twice (rejections are remembered).</summary>
    public string Fingerprint { get; set; } = "";
    public Guid? WorkItemId { get; set; }
    public Guid? ActionItemId { get; set; }
    public Guid? DecisionId { get; set; }
    public Guid? BlockerId { get; set; }
    public Guid? CodeEventId { get; set; }

    public string? ProposedTitle { get; set; }
    public WorkItemStatus? ProposedStatus { get; set; }
    public Guid? ProposedOwnerId { get; set; }
    public DateOnly? ProposedDueDate { get; set; }
    public string? ProposedRelease { get; set; }
    public double? ProposedEstimateDays { get; set; }
    public WorkRole? ProposedRole { get; set; }

    public Guid? EvidenceId { get; set; }
    public double Confidence { get; set; }
    public SuggestionOutcome Outcome { get; set; } = SuggestionOutcome.Pending;
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
}

public class CodeEvent : Entity
{
    public Guid ProjectId { get; set; }
    public CodeEventType Type { get; set; }
    public string ExternalId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public string Branch { get; set; } = "";
    /// <summary>Comma separated ticket keys found in the title/branch.</summary>
    public string TicketRefs { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string Url { get; set; } = "";

    public IEnumerable<string> Refs() =>
        TicketRefs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>Audit of every extraction: prompt version, model and output, so results can be re-run.</summary>
public class ExtractionLog : Entity
{
    public Guid MeetingId { get; set; }
    public string Extractor { get; set; } = "";
    public string Model { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public int InputChars { get; set; }
    public int DecisionsFound { get; set; }
    public int ActionsFound { get; set; }
    public int BlockersFound { get; set; }
    public long DurationMs { get; set; }
    public string OutputJson { get; set; } = "";
}

public class SimulationRun : Entity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string ScenarioJson { get; set; } = "";
    public int Iterations { get; set; }
    public int Seed { get; set; }
    public DateOnly BaselineP50 { get; set; }
    public DateOnly BaselineP80 { get; set; }
    public DateOnly BaselineP95 { get; set; }
    public DateOnly ScenarioP50 { get; set; }
    public DateOnly ScenarioP80 { get; set; }
    public DateOnly ScenarioP95 { get; set; }
    public double BaselineOnTime { get; set; }
    public double ScenarioOnTime { get; set; }
    public string Narrative { get; set; } = "";
    public string ResultJson { get; set; } = "";
}

public class AuditEntry : Entity
{
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Details { get; set; } = "";
}

public class TenantSettings : Entity
{
    public string TeamName { get; set; } = "My Team";
    public int RetentionDays { get; set; } = 30;
    public bool CaptureEnabledByDefault { get; set; } = true;
    public string DataRegion { get; set; } = "Central India";
    public int StaleAfterDays { get; set; } = 7;
    /// <summary>Extracted items below this confidence are dropped; nothing is ever auto-applied.</summary>
    public double MinConfidence { get; set; } = 0.45;
}
