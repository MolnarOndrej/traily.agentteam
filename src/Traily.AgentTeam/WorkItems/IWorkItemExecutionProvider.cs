namespace Traily.AgentTeam.WorkItems;

public sealed record WorkItemExecutionRequest(
    string AgentId,
    string WorkItemReference,
    string TaskSnapshot,
    string WorkingDirectory);

public sealed record WorkItemExecutionResult(
    bool Completed,
    WorkItemStopReason? StopReason);

public interface IWorkItemExecutionProvider
{
    string ProviderId { get; }

    Task<WorkItemExecutionResult> RunAsync(
        WorkItemExecutionRequest request,
        Func<string, CancellationToken, Task> onSessionAvailable,
        CancellationToken cancellationToken);
}