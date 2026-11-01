using MonteCarlo.Domain.Common;

namespace MonteCarlo.Domain.Entities;

public class Project : Entity
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly Deadline { get; set; }
    public string CurrentRelease { get; set; } = "v1";
}

public class Person : Entity
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    /// <summary>Teams / Entra user id used to map transcript speakers.</summary>
    public string? ExternalUserId { get; set; }
    public WorkRole Role { get; set; }
    public string Skills { get; set; } = "";
    /// <summary>Share of a full day available for project work (0..1).</summary>
    public double Capacity { get; set; } = 1.0;
    public DateOnly StartDate { get; set; }
    public DateOnly? LeaveDate { get; set; }

    public bool IsActiveOn(DateOnly day) => StartDate <= day && (LeaveDate is null || LeaveDate > day);

    public bool MatchesName(string? spoken)
    {
        if (string.IsNullOrWhiteSpace(spoken)) return false;
        var s = spoken.Trim();
        if (Name.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
        var first = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var spokenFirst = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return first.Length > 0 && first.Equals(spokenFirst, StringComparison.OrdinalIgnoreCase);
    }
}

public class WorkItem : Entity
{
    public Guid ProjectId { get; set; }
    /// <summary>Key in the ticket system, e.g. GST-142.</summary>
    public string ExternalRef { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Todo;
    public WorkRole Role { get; set; } = WorkRole.Backend;
    public double? EstimateDays { get; set; }
    public double? ActualDays { get; set; }
    public Guid? OwnerId { get; set; }
    public string Release { get; set; } = "v1";
    /// <summary>1 = highest, 5 = lowest.</summary>
    public int Priority { get; set; } = 3;
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public List<WorkItemDependency> Dependencies { get; set; } = [];

    public void Touch(DateTimeOffset at) => LastActivityAt = at;

    public void ChangeStatus(WorkItemStatus status, DateTimeOffset at)
    {
        Touch(at);
        if (Status == status) return;
        Status = status;
        if (status == WorkItemStatus.InProgress && StartedAt is null) StartedAt = at;
        if (status == WorkItemStatus.Done)
        {
            CompletedAt = at;
            StartedAt ??= at;
            var days = WorkingCalendar.WorkingDaysBetween(
                DateOnly.FromDateTime(StartedAt.Value.UtcDateTime), DateOnly.FromDateTime(at.UtcDateTime));
            ActualDays ??= Math.Max(0.5, days);
        }
    }
}

public class WorkItemDependency
{
    public Guid WorkItemId { get; set; }
    public Guid DependsOnId { get; set; }
}

public class Meeting : Entity
{
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public MeetingSource Source { get; set; } = MeetingSource.Upload;
    /// <summary>Teams meeting id / transcript id when it came from Graph.</summary>
    public string? ExternalId { get; set; }
    /// <summary>Raw transcript; deleted after the retention period, extracted facts stay.</summary>
    public string? RawTranscript { get; set; }
    public DateTimeOffset? RawDeletedAt { get; set; }
    public bool OptedOut { get; set; }
    public bool Processed { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public List<SpeakerTurn> Turns { get; set; } = [];
}

public class SpeakerTurn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MeetingId { get; set; }
    public int Sequence { get; set; }
    public int OffsetSeconds { get; set; }
    public string SpeakerName { get; set; } = "";
    public Guid? PersonId { get; set; }
    public string Text { get; set; } = "";
}
