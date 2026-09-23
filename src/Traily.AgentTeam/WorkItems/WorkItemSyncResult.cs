namespace Traily.AgentTeam.WorkItems;

public enum WorkItemSyncOutcome
{
    New,
    AlreadyQueued,
    Updated,
    Unmapped,
    AgentInactive,
    AssignmentChanged,
    Unchanged
}

public sealed record WorkItemSyncResult(
    string WorkItemReference,
    WorkItemSyncOutcome Outcome,
    string? AgentId,
    WorkItemJobStatus? Status);