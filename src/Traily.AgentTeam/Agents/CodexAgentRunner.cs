using Traily.AgentTeam.Runtime;

namespace Traily.AgentTeam.Agents;

public sealed class CodexAgentRunner : IAgentRunner
{
    private readonly CodexProcessClient _processClient;
    private readonly CodexResponseParser _responseParser;

    public CodexAgentRunner(
        CodexProcessClient processClient,
        CodexResponseParser responseParser)
    {
        ArgumentNullException.ThrowIfNull(processClient);
        ArgumentNullException.ThrowIfNull(responseParser);

        _processClient = processClient;
        _responseParser = responseParser;
    }

    public async Task<AgentResult> RunAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.Instructions);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.WorkingDirectory);

        var processResult = await _processClient.ExecuteAsync(
            request.WorkingDirectory,
            request.Instructions,
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