namespace Traily.AgentTeam.Agents;

public interface IAgentCatalog
{
    AgentDefinition GetRequired(string agentId);
}