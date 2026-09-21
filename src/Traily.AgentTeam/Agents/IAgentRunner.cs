namespace Traily.AgentTeam.Agents;

public interface IAgentRunner
{
    Task<AgentResult> RunAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default);
}

public record AgentRequest(
    string AgentRole,
    string TaskId,
    string Instructions,
    string WorkingDirectory);

public record AgentResult(
    bool Success,
    string Output);