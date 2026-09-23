using Traily.AgentTeam.Agents;

namespace Traily.AgentTeam.WorkItems;

public sealed class WorkItemJob
{
    public Guid Id { get; set; }

    public string SourceId { get; set; } = string.Empty;

    public string ExternalWorkItemId { get; set; } = string.Empty;

    public string WorkItemReference { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ExternalAssigneeId { get; set; } = string.Empty;

    public string AgentId { get; set; } = string.Empty;

    public WorkItemJobStatus Status { get; set; }

    public DateTimeOffset SourceUpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public AgentProfile Agent { get; set; } = null!;
}