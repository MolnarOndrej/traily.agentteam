namespace Traily.AgentTeam.Agents;

public interface IAgentRunner
{
    Task<AgentResult> RunAsync(
        AgentRequest request,
        Func<string, CancellationToken, Task>?
            onSessionAvailable = null,
        CancellationToken cancellationToken = default);
}

public record AgentRequest(
    string AgentId,
    string TaskId,
    string Instructions,
    string WorkingDirectory);

public record AgentResult(
    bool Success,
    string Output);