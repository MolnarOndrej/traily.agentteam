namespace Traily.AgentTeam.Agents;

public sealed class AgentProfile
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public int MaxConcurrentJobs { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<AgentSkill> Skills { get; } =
        new List<AgentSkill>();

    public ICollection<AgentExternalIdentity> ExternalIdentities { get; } =
        new List<AgentExternalIdentity>();
}