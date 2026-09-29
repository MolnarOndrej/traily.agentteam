namespace Traily.AgentTeam.Git;

public sealed class GitRepository
{
    public string Id { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RemoteUrl { get; set; } = string.Empty;
    public string BaseBranch { get; set; } = string.Empty;
    public string? LocalPathOverride { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ManagedProject Project { get; set; } = null!;
}