namespace MonteCarlo.Domain.Common;

public enum WorkItemStatus { Backlog, Todo, InProgress, Blocked, InReview, Done, Cancelled }

public enum WorkRole { Backend, Frontend, FullStack, QA, DevOps, Design, Product }

public enum SourceType { Meeting, Chat, Ticket, Commit, PullRequest, Build, Manual }

public enum SuggestionType
{
    CreateTicket,
    UpdateStatus,
    CloseTicket,
    MoveRelease,
    AssignOwner,
    AddEstimate,
    StaleTicket,
    LinkConversation
}

public enum SuggestionOutcome { Pending, Accepted, Edited, Rejected }

public enum ActionItemState { Proposed, Accepted, Rejected, Done }

public enum CodeEventType { Commit, PullRequestOpened, PullRequestMerged, BuildFailed, BuildSucceeded, Deployed }

public enum MeetingSource { Upload, TeamsTranscript, Chat }

public static class WorkItemStatusExtensions
{
    public static bool IsOpen(this WorkItemStatus s) => s is not (WorkItemStatus.Done or WorkItemStatus.Cancelled);
}
