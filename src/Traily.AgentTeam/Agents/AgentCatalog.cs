namespace Traily.AgentTeam.Agents;

public sealed class AgentCatalog : IAgentCatalog
{
    private readonly IReadOnlyDictionary<string, AgentDefinition> _agents;

    public AgentCatalog()
    {
        var teamLead = new AgentDefinition(
            Id: "team-lead",
            Role: "Team Lead",
            InstructionsPath: Path.Combine(
                AgentFileConventions.DefinitionsDirectory,
                "team-lead",
                AgentFileConventions.AgentInstructionsFileName),
            SkillIds: ["task-analysis"]);

        _agents = new Dictionary<string, AgentDefinition>(
            StringComparer.OrdinalIgnoreCase)
        {
            [teamLead.Id] = teamLead
        };
    }

    public AgentDefinition GetRequired(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        if (!_agents.TryGetValue(agentId, out var definition))
        {
            throw new KeyNotFoundException(
                $"Agent '{agentId}' is not registered.");
        }

        return definition;
    }
}