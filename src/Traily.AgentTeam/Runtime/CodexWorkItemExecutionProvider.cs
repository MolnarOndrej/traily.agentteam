using Traily.AgentTeam.Orchestration;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Runtime;

public sealed class CodexWorkItemExecutionProvider(
    AgentTaskInvoker invoker)
    : IWorkItemExecutionProvider
{
    public string ProviderId => "codex-cli";

    public async Task<WorkItemExecutionResult> RunAsync(
        WorkItemExecutionRequest request,
        Func<string, CancellationToken, Task>
            onSessionAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(onSessionAvailable);

        var result = await invoker.InvokeAsync(
            new AgentTaskInvocation(
                request.AgentId,
                request.WorkItemReference,
                request.TaskSnapshot,
                request.WorkingDirectory),
            onSessionAvailable,
            cancellationToken);

        return result.Success
            ? new WorkItemExecutionResult(true, null)
            : new WorkItemExecutionResult(
                false,
                WorkItemStopReason.ProviderFailed);
    }
}