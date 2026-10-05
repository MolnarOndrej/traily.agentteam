using Traily.AgentTeam.WorkSources;

namespace Traily.AgentTeam.Git;

public sealed class ManagedProject
{
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string WorkSourceConnectionId { get; set; } = string.Empty;
    public WorkSourceConnection WorkSourceConnection { get; set; } = null!;
    public string ExternalProjectId { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
