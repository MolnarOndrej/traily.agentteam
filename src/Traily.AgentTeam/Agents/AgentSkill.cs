namespace Traily.AgentTeam.Agents;

public sealed class AgentSkill
{
    public long Id { get; set; }

    public string AgentId { get; set; } = string.Empty;

    public string SkillId { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public AgentProfile Agent { get; set; } = null!;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}