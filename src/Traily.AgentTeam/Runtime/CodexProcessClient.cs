using System.Diagnostics;

namespace Traily.AgentTeam.Runtime;

public sealed record CodexProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

public sealed class CodexProcessClient
{
    public async Task<CodexProcessResult> ExecuteAsync(
        string workingDirectory,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        var fullPath = Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(
                $"Working directory not found: {fullPath}");
        }

        var startInfo = CreateStartInfo(fullPath);

        using var process = new Process
        {
            StartInfo = startInfo
        };

        cancellationToken.ThrowIfCancellationRequested();

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Failed to start Codex CLI.");
        }

        try
        {
            return await ExecuteStartedProcessAsync(
                process,
                prompt,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(
                CancellationToken.None);

            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string workingDirectory)
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

        foreach (var argument in new[]
        {
            "/d",
            "/c",
            "codex.cmd",
            "exec",
            "--sandbox", "read-only",
            "--json",
            "--ephemeral",
            "--ignore-user-config",
            "-C", workingDirectory,
            "-"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static async Task<CodexProcessResult> ExecuteStartedProcessAsync(
        Process process,
        string prompt,
        CancellationToken cancellationToken)
    {
        var outputTask = process.StandardOutput.ReadToEndAsync(
            cancellationToken);

        var errorTask = process.StandardError.ReadToEndAsync(
            cancellationToken);

        await process.StandardInput.WriteAsync(
            prompt.AsMemory(),
            cancellationToken);

        process.StandardInput.Close();

        await process.WaitForExitAsync(cancellationToken);

        return new CodexProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }
}