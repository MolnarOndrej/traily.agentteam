namespace Traily.AgentTeam.Runtime;

public interface IExecutionTraceWriter
{
    Task WriteAsync(
        CodexProcessResult result,
        CancellationToken cancellationToken = default);
}