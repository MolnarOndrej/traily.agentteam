namespace Traily.AgentTeam.Agents;

public sealed record AgentSkillDefinition(
    string Id,
    string Instructions);

public sealed record AgentDefinition(
    string Id,
    string Name,
    string Instructions,
    int MaxConcurrentJobs,
    IReadOnlyList<AgentSkillDefinition> Skills);

public sealed record AgentSummary(
    string Id,
    string Name,
    bool IsEnabled,
    int MaxConcurrentJobs,
    int SkillCount);