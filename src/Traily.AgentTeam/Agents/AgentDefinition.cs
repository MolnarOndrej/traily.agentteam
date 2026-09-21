namespace Traily.AgentTeam.Agents;

public sealed record AgentDefinition(
    string Id,
    string Role,
    string InstructionsPath,
    IReadOnlyList<string> SkillIds);