namespace Traily.AgentTeam.Agents;

public interface IAgentCatalog
{
    Task<AgentDefinition> GetRequiredAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentSummary>> ListAsync(
        CancellationToken cancellationToken = default);
}