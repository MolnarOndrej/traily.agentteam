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
        CodexProcessResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        Directory.CreateDirectory(_traceDirectory);

        var executionId = CreateExecutionId();

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

    private static string CreateExecutionId()
    {
        var timestamp = DateTimeOffset.UtcNow
            .ToString("yyyyMMddTHHmmssfffZ");

        return $"{timestamp}_{Guid.NewGuid():N}";
    }
}