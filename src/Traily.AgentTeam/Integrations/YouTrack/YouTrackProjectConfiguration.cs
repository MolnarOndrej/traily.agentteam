namespace Traily.AgentTeam.Integrations.YouTrack;

public sealed class YouTrackProjectConfiguration
{
    public string SourceId { get; set; } = string.Empty;
    // Values are supplied from the mapped project and agent accounts, not the token owner.
    public string DiscoveryQueryTemplate { get; set; } =
        "project: {{project}} {{stateField}}: {To Do} {{assigneeField}}: {{assignee}} sort by: issue id asc";
    public string WorkflowStateField { get; set; } = "Stage";
    public string AssigneeField { get; set; } = "Assignee";
    public DateTimeOffset UpdatedAt { get; set; }
}
