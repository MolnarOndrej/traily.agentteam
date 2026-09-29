using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;

namespace Traily.AgentTeam.Runtime;

public sealed record CodexProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public interface ICodexProcessClient
{
    Task<CodexProcessResult> ExecuteAsync(
        string workingDirectory,
        string prompt,
        Func<string, CancellationToken, Task>? onStandardOutput,
        Func<string, CancellationToken, Task>? onStandardError,
        CancellationToken cancellationToken = default);

    Task<CodexProcessResult> ResumeAsync(
        string workingDirectory,
        string sessionId,
        string prompt,
        Func<string, CancellationToken, Task>? onStandardOutput,
        Func<string, CancellationToken, Task>? onStandardError,
        CancellationToken cancellationToken = default);
}

public sealed class CodexProcessClient : ICodexProcessClient
{
    public Task<CodexProcessResult> ExecuteAsync(
        string workingDirectory,
        string prompt,
        Func<string, CancellationToken, Task>? onStandardOutput,
        Func<string, CancellationToken, Task>? onStandardError,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            workingDirectory, null, prompt,
            onStandardOutput, onStandardError, cancellationToken);
    }

    public Task<CodexProcessResult> ResumeAsync(
        string workingDirectory,
        string sessionId,
        string prompt,
        Func<string, CancellationToken, Task>? onStandardOutput,
        Func<string, CancellationToken, Task>? onStandardError,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return RunAsync(
            workingDirectory, sessionId, prompt,
            onStandardOutput, onStandardError, cancellationToken);
    }

    private async Task<CodexProcessResult> RunAsync(
        string workingDirectory,
        string? sessionId,
        string prompt,
        Func<string, CancellationToken, Task>? onStandardOutput,
        Func<string, CancellationToken, Task>? onStandardError,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var fullPath = Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Working directory not found: {fullPath}");
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(fullPath, sessionId)
        };

        cancellationToken.ThrowIfCancellationRequested();

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Failed to start Codex CLI.");
        }

        return await ExecuteStartedProcessAsync(
            process, prompt, onStandardOutput,
            onStandardError, cancellationToken);
    }

    private static ProcessStartInfo CreateStartInfo(
        string workingDirectory,
        string? sessionId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var arguments = sessionId is null
            ? new[]
            {
                "/d", "/c", "codex.cmd", "exec",
                "--sandbox", "read-only",
                "--json", "--ignore-user-config",
                "-C", workingDirectory, "-"
            }
            : new[]
            {
                "/d", "/c", "codex.cmd", "exec", "resume",
                "--json", "--ignore-user-config",
                "-c", "sandbox_mode=read-only",
                sessionId, "-"
            };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<CodexProcessResult>
        ExecuteStartedProcessAsync(
            Process process,
            string prompt,
            Func<string, CancellationToken, Task>? onStandardOutput,
            Func<string, CancellationToken, Task>? onStandardError,
            CancellationToken cancellationToken)
    {
        using var stopSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var callbackFailure =
            new TaskCompletionSource<ExceptionDispatchInfo>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        // These readers keep draining after a callback fails. The process
        // is stopped by the linked token and cleaned up below.
        var outputTask = ReadOutputAsync(
            process.StandardOutput,
            onStandardOutput,
            stopSource,
            callbackFailure);

        var errorTask = ReadOutputAsync(
            process.StandardError,
            onStandardError,
            stopSource,
            callbackFailure);

        try
        {
            await process.StandardInput.WriteAsync(
                prompt.AsMemory(),
                stopSource.Token);

            process.StandardInput.Close();

            await process.WaitForExitAsync(stopSource.Token);
            await Task.WhenAll(outputTask, errorTask);

            if (callbackFailure.Task.IsCompletedSuccessfully)
            {
                callbackFailure.Task.Result.Throw();
            }

            return new CodexProcessResult(
                process.ExitCode,
                outputTask.Result,
                errorTask.Result);
        }
        catch
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                    when (process.HasExited)
                {
                    // The process exited between the check and Kill.
                }
            }

            await process.WaitForExitAsync(CancellationToken.None);

            // Killing the process closes its redirected pipes. Always
            // finish observing both readers before disposing Process.
            try
            {
                await Task.WhenAll(outputTask, errorTask);
            }
            catch
            {
                // Preserve the cancellation or original failure below.
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (callbackFailure.Task.IsCompletedSuccessfully)
            {
                callbackFailure.Task.Result.Throw();
            }

            throw;
        }
    }

    private static async Task<string> ReadOutputAsync(
        StreamReader reader,
        Func<string, CancellationToken, Task>? onOutput,
        CancellationTokenSource stopSource,
        TaskCompletionSource<ExceptionDispatchInfo> callbackFailure)
    {
        var output = new StringBuilder();

        // Do not cancel pipe reads: after stopping the child, drain
        // what it already wrote and wait for the pipe to close.
        while (await reader.ReadLineAsync() is { } line)
        {
            var outputLine = line + Environment.NewLine;
            output.Append(outputLine);

            if (onOutput is null ||
                callbackFailure.Task.IsCompleted)
            {
                continue;
            }

            try
            {
                await onOutput(outputLine, stopSource.Token);
            }
            catch (Exception exception)
            {
                callbackFailure.TrySetResult(
                    ExceptionDispatchInfo.Capture(exception));

                stopSource.Cancel();
            }
        }

        return output.ToString();
    }
}