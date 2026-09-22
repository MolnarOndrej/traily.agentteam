namespace Traily.AgentTeam.Runtime;

public sealed class FileExecutionTraceWriter : IExecutionTraceWriter
{
    private readonly string _traceDirectory;

    public FileExecutionTraceWriter(string traceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            traceDirectory);

        _traceDirectory = Path.GetFullPath(traceDirectory);
    }

    public async Task<ExecutionTraceHandle> StartAsync(
        ExecutionTraceContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        ArgumentException.ThrowIfNullOrWhiteSpace(context.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.TaskId);

        Directory.CreateDirectory(_traceDirectory);

        var trace = new ExecutionTraceHandle(CreateExecutionId(context));

        await File.WriteAllTextAsync(
            GetOutputPath(trace),
            string.Empty,
            cancellationToken);

        return trace;
    }

    public Task AppendStandardOutputAsync(
        ExecutionTraceHandle trace,
        string output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(output);

        return File.AppendAllTextAsync(
            GetOutputPath(trace),
            output,
            cancellationToken);
    }

    public Task AppendStandardErrorAsync(
        ExecutionTraceHandle trace,
        string error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(error);

        return File.AppendAllTextAsync(
            GetErrorPath(trace),
            error,
            cancellationToken);
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

    private string GetOutputPath(
        ExecutionTraceHandle trace)
    {
        return Path.Combine(
            _traceDirectory,
            $"{trace.ExecutionId}.stdout.log");
    }

    private string GetErrorPath(
        ExecutionTraceHandle trace)
    {
        return Path.Combine(
            _traceDirectory,
            $"{trace.ExecutionId}.stderr.log");
    }
}