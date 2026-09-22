namespace Traily.AgentTeam.Runtime;

public sealed record ExecutionTraceContext(
    string AgentId,
    string TaskId);

public sealed record ExecutionTraceHandle(
    string ExecutionId);

public interface IExecutionTraceWriter
{
    Task<ExecutionTraceHandle> StartAsync(
        ExecutionTraceContext context,
        CancellationToken cancellationToken = default);

    Task AppendStandardOutputAsync(
        ExecutionTraceHandle trace,
        string output,
        CancellationToken cancellationToken = default);

    Task AppendStandardErrorAsync(
        ExecutionTraceHandle trace,
        string error,
        CancellationToken cancellationToken = default);
}