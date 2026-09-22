namespace Traily.AgentTeam.Agents;

public sealed class AgentExternalIdentity
{
    public long Id { get; set; }

    public string AgentId { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string ExternalUserId { get; set; } = string.Empty;

    public string Login { get; set; } = string.Empty;

    public string? DisplayName { get; set; }

    public AgentProfile Agent { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}