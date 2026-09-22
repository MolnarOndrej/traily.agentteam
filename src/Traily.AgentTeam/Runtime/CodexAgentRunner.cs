using Traily.AgentTeam.Agents;

namespace Traily.AgentTeam.Runtime;

public sealed class CodexAgentRunner : IAgentRunner
{
    private readonly CodexProcessClient _processClient;
    private readonly CodexResponseParser _responseParser;
    private readonly IExecutionTraceWriter _traceWriter;

    public CodexAgentRunner(
        CodexProcessClient processClient,
        CodexResponseParser responseParser,
        IExecutionTraceWriter traceWriter)
    {
        ArgumentNullException.ThrowIfNull(processClient);
        ArgumentNullException.ThrowIfNull(responseParser);
        ArgumentNullException.ThrowIfNull(traceWriter);

        _processClient = processClient;
        _responseParser = responseParser;
        _traceWriter = traceWriter;
    }

    public async Task<AgentResult> RunAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Instructions);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TaskId);

        var trace = await _traceWriter.StartAsync(
            new ExecutionTraceContext(
            request.AgentId,
            request.TaskId),
            cancellationToken);

        var processResult = await _processClient.ExecuteAsync(
            request.WorkingDirectory,
            request.Instructions,
            (output, token) => _traceWriter.AppendStandardOutputAsync(
                trace,
                output,
                token),
            (error, token) => _traceWriter.AppendStandardErrorAsync(
                trace,
                error,
                token),
            cancellationToken);

        if (processResult.ExitCode != 0)
        {
            return new AgentResult(
                false,
                $"Codex exited with code {processResult.ExitCode}.");
        }

        var response = _responseParser.Parse(
            processResult.StandardOutput);

        return new AgentResult(
            response.Success,
            response.Output);
    }
}