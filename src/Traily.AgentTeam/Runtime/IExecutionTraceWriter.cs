namespace Traily.AgentTeam.Runtime;

public sealed record ExecutionTraceContext(
    string AgentId,
    string TaskId);

public interface IExecutionTraceWriter
{
    Task WriteAsync(
        ExecutionTraceContext context,
        CodexProcessResult result,
        CancellationToken cancellationToken = default);
}