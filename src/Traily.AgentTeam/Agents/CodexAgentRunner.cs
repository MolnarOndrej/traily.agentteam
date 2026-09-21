using System.Diagnostics;
using System.Text.Json;

namespace Traily.AgentTeam.Agents;

public sealed class CodexAgentRunner : IAgentRunner
{
    private readonly string _workingDirectory;

    public CodexAgentRunner(string workingDirectory)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(_workingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Working directory not found: {_workingDirectory}");
        }
    }

    public async Task<AgentResult> RunAsync(
        AgentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.AgentRole != "Team Lead")
        {
            return new AgentResult(
                false,
                $"Unsupported agent role: {request.AgentRole}");
        }

        var prompt = $"""
            You are Traily's AI software development Team Lead.

            Your responsibilities:
            - Analyze development tasks.
            - Identify requirements and dependencies.
            - Propose implementation steps.
            - Identify testing and review requirements.
            - Recommend which specialist should handle the task.

            Current operating restrictions:
            - You are operating in analysis-only mode.
            - Do not modify files or execute development tasks.
            - Do not create branches, commits, or pull requests.
            - Do not change YouTrack tasks or their statuses.
            - Do not merge or release software.
            - Do not read files outside the specified working directory.
            - Do not request elevated permissions.

            Task ID: {request.TaskId}

            Task instructions:
            {request.Instructions}

            Provide a clear, actionable analysis of this task.
            """;

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        // cmd.exe launches the npm-installed codex.cmd on Windows.
        // No task content is passed as a command-line argument.
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
            "-C", _workingDirectory,
            "-"
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            return new AgentResult(
                false,
                "Failed to start Codex CLI.");
        }

        // Read both streams concurrently to avoid pipe deadlocks.
        var outputTask =
            process.StandardOutput.ReadToEndAsync(cancellationToken);

        var errorTask =
            process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.StandardInput.WriteAsync(
                prompt.AsMemory(),
                cancellationToken);

            process.StandardInput.Close();

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;

            // Consume stderr without exposing potentially
            // sensitive diagnostic information in agent output.
            _ = await errorTask;

            if (process.ExitCode != 0)
            {
                return new AgentResult(
                    false,
                    $"Codex exited with code {process.ExitCode}.");
            }

            string? finalMessage = null;
            bool completed = false;
            bool failed = false;

            foreach (var line in output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                using var document = JsonDocument.Parse(line);

                var root = document.RootElement;

                if (!root.TryGetProperty("type", out var typeElement))
                    continue;

                var eventType = typeElement.GetString();

                if (eventType == "turn.completed")
                {
                    completed = true;
                }
                else if (eventType == "turn.failed")
                {
                    failed = true;
                }
                else if (eventType == "item.completed" &&
                    root.TryGetProperty("item", out var item) &&
                    item.TryGetProperty("type", out var itemType) &&
                    itemType.GetString() == "agent_message" &&
                    item.TryGetProperty("text", out var text))
                {
                    finalMessage = text.GetString();
                }
            }

            if (failed || !completed ||
                string.IsNullOrWhiteSpace(finalMessage))
            {
                return new AgentResult(
                    false,
                    "Codex did not return a completed agent response.");
            }

            return new AgentResult(true, finalMessage);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }
    }
}