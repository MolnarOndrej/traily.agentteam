namespace Traily.AgentTeam.Runtime;

public sealed class CodexExecutionTraceWriter : IExecutionTraceWriter
{
    private readonly string _traceDirectory;

    public CodexExecutionTraceWriter(string traceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            traceDirectory);

        _traceDirectory = Path.GetFullPath(traceDirectory);
    }

    public async Task WriteAsync(
        ExecutionTraceContext context,
        CodexProcessResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(result);

        ArgumentException.ThrowIfNullOrWhiteSpace(context.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TaskId);

        Directory.CreateDirectory(_traceDirectory);

        var executionId = CreateExecutionId(context);

        var outputPath = Path.Combine(
            _traceDirectory,
            $"{executionId}.jsonl");

        await File.WriteAllTextAsync(
            outputPath,
            result.StandardOutput,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            var errorPath = Path.Combine(
                _traceDirectory,
                $"{executionId}.stderr.log");

            await File.WriteAllTextAsync(
                errorPath,
                result.StandardError,
                cancellationToken);
        }
    }

    private static string CreateExecutionId(
        ExecutionTraceContext context)
    {
        var timestamp = DateTimeOffset.UtcNow
            .ToString("yyyyMMddTHHmmssfffZ");

        var agentId = MakeFileNameSafe(context.AgentId);
        var taskId = MakeFileNameSafe(context.TaskId);

        return $"{timestamp}_{agentId}_{taskId}_{Guid.NewGuid():N}";
    }

    private static string MakeFileNameSafe(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();

        return string.Concat(
            value.Select(character =>
                invalidCharacters.Contains(character)
                    ? '_'
                    : character));
    }
}