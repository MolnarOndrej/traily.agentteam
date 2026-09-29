using Traily.AgentTeam.Agents;

namespace Traily.AgentTeam.Runtime;

public sealed record CodexContinuationRequest(
    string AgentId,
    string TaskId,
    string WorkingDirectory,
    string SessionId,
    string ContinuationPrompt);

public sealed class CodexAgentRunner : IAgentRunner
{
    private readonly ICodexProcessClient _processClient;
    private readonly CodexResponseParser _responseParser;
    private readonly IExecutionTraceWriter _traceWriter;

    public CodexAgentRunner(
        ICodexProcessClient processClient,
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

    public Task<AgentResult> RunAsync(
        AgentRequest request,
        Func<string, CancellationToken, Task>? onSessionAvailable = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RunCoreAsync(
            request.AgentId,
            request.TaskId,
            request.WorkingDirectory,
            request.Instructions,
            expectedSessionId: null,
            onSessionAvailable,
            cancellationToken);
    }

    public Task<AgentResult> ResumeAsync(
        CodexContinuationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SessionId);

        return RunCoreAsync(
            request.AgentId,
            request.TaskId,
            request.WorkingDirectory,
            request.ContinuationPrompt,
            request.SessionId,
            onSessionAvailable: null,
            cancellationToken);
    }

    private async Task<AgentResult> RunCoreAsync(
        string agentId,
        string taskId,
        string workingDirectory,
        string prompt,
        string? expectedSessionId,
        Func<string, CancellationToken, Task>? onSessionAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var trace = await _traceWriter.StartAsync(
            new ExecutionTraceContext(agentId, taskId),
            cancellationToken);

        var outputHandler = new CodexOutputHandler(
            _responseParser,
            _traceWriter,
            trace,
            onSessionAvailable,
            expectedSessionId);

        var processResult = expectedSessionId is null
            ? await _processClient.ExecuteAsync(
                workingDirectory,
                prompt,
                outputHandler.OnStandardOutputAsync,
                outputHandler.OnStandardErrorAsync,
                cancellationToken)
            : await _processClient.ResumeAsync(
                workingDirectory,
                expectedSessionId,
                prompt,
                outputHandler.OnStandardOutputAsync,
                outputHandler.OnStandardErrorAsync,
                cancellationToken);

        if (processResult.ExitCode != 0)
        {
            return new AgentResult(
                false,
                $"Codex exited with code {processResult.ExitCode}.");
        }

        if (expectedSessionId is not null &&
            !outputHandler.ExpectedSessionObserved)
        {
            throw new InvalidDataException(
                "Codex resume did not report a session ID.");
        }

        var response = _responseParser.Parse(
            processResult.StandardOutput);

        return new AgentResult(
            response.Success,
            response.Output);
    }

    private sealed class CodexOutputHandler(
        CodexResponseParser responseParser,
        IExecutionTraceWriter traceWriter,
        ExecutionTraceHandle trace,
        Func<string, CancellationToken, Task>? onSessionAvailable,
        string? expectedSessionId)
    {
        public bool ExpectedSessionObserved { get; private set; }

        public async Task OnStandardOutputAsync(
            string output,
            CancellationToken cancellationToken)
        {
            var sessionId =
                responseParser.GetStartedSessionId(output);

            if (sessionId is not null)
            {
                if (expectedSessionId is not null)
                {
                    if (!string.Equals(
                        sessionId,
                        expectedSessionId,
                        StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "Codex resumed a different session than requested.");
                    }

                    ExpectedSessionObserved = true;
                }

                if (onSessionAvailable is not null)
                {
                    await onSessionAvailable(
                        sessionId,
                        cancellationToken);
                }
            }

            await traceWriter.AppendStandardOutputAsync(
                trace,
                output,
                cancellationToken);
        }

        public Task OnStandardErrorAsync(
            string error,
            CancellationToken cancellationToken)
        {
            return traceWriter.AppendStandardErrorAsync(
                trace,
                error,
                cancellationToken);
        }
    }
}