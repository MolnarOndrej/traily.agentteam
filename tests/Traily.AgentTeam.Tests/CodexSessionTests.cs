using System.Diagnostics;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Runtime;

namespace Traily.AgentTeam.Tests;

public sealed class CodexSessionTests
{
    [Fact]
    public async Task RunnerReportsSessionBeforeProcessingLaterOutput()
    {
        var traceDirectory = Path.Combine(
            Path.GetTempPath(),
            $"traily-codex-test-{Guid.NewGuid():N}");

        try
        {
            string? savedSessionId = null;

            var processClient = new FakeCodexProcessClient(
                () => savedSessionId == "session-1");

            var runner = new CodexAgentRunner(
                processClient,
                new CodexResponseParser(),
                new FileExecutionTraceWriter(traceDirectory));

            var result = await runner.RunAsync(
                new AgentRequest(
                    AgentId: "team-lead",
                    TaskId: "TEST-1",
                    Instructions: "Analyze this test task.",
                    WorkingDirectory: Path.GetTempPath()),
                (sessionId, _) =>
                {
                    savedSessionId = sessionId;
                    return Task.CompletedTask;
                });

            Assert.Equal("session-1", savedSessionId);
            Assert.True(result.Success);
            Assert.Equal("Done", result.Output);
        }
        finally
        {
            if (Directory.Exists(traceDirectory))
            {
                Directory.Delete(
                    traceDirectory,
                    recursive: true);
            }
        }
    }

    [Fact]
    public async Task CallbackFailureStopsCodexProcessPromptly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(
            Path.GetTempPath(),
            $"traily-codex-process-{Guid.NewGuid():N}");

        Directory.CreateDirectory(directory);

        try
        {
            var commandPath = Path.Combine(directory, "codex.cmd");
            var markerPath = Path.Combine(directory, "finished.txt");

            File.WriteAllText(
                commandPath,
                string.Join("\r\n", new[]
                {
                "@echo off",
                "echo {\"type\":\"thread.started\",\"thread_id\":\"test-session\"}",
                "ping -n 30 127.0.0.1 >nul",
                "echo finished> finished.txt"
                }) + "\r\n");

            var stopwatch = Stopwatch.StartNew();
            var client = new CodexProcessClient();

            var exception = await Assert.ThrowsAsync<
                InvalidOperationException>(() =>
                client.ExecuteAsync(
                    directory,
                    "Test prompt",
                    (_, _) => throw new InvalidOperationException(
                        "Session persistence failed."),
                    null));

            stopwatch.Stop();

            Assert.Equal(
                "Session persistence failed.",
                exception.Message);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Process took {stopwatch.Elapsed} to stop.");
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("session-1", true)]
    [InlineData("different-session", false)]
    public async Task ResumeUsesContinuationPromptAndChecksSession(
    string emittedSessionId,
    bool shouldSucceed)
    {
        var traceDirectory = Path.Combine(
            Path.GetTempPath(),
            $"traily-resume-test-{Guid.NewGuid():N}");

        try
        {
            var processClient =
                new FakeResumeProcessClient(emittedSessionId);

            var runner = new CodexAgentRunner(
                processClient,
                new CodexResponseParser(),
                new FileExecutionTraceWriter(traceDirectory));

            var request = new CodexContinuationRequest(
                AgentId: "team-lead",
                TaskId: "TEST-1",
                WorkingDirectory: Path.GetTempPath(),
                SessionId: "session-1",
                ContinuationPrompt: "Inspect the current state and continue.");

            if (shouldSucceed)
            {
                var result = await runner.ResumeAsync(request);

                Assert.True(result.Success);
                Assert.Equal("Continued", result.Output);
            }
            else
            {
                await Assert.ThrowsAsync<InvalidDataException>(
                    () => runner.ResumeAsync(request));
            }

            Assert.Equal("session-1", processClient.RequestedSessionId);
            Assert.Equal(
                "Inspect the current state and continue.",
                processClient.ReceivedPrompt);
        }
        finally
        {
            if (Directory.Exists(traceDirectory))
            {
                Directory.Delete(traceDirectory, recursive: true);
            }
        }
    }

    private sealed class FakeResumeProcessClient(
        string emittedSessionId) : ICodexProcessClient
    {
        public string? RequestedSessionId { get; private set; }
        public string? ReceivedPrompt { get; private set; }

        public Task<CodexProcessResult> ExecuteAsync(
            string workingDirectory,
            string prompt,
            Func<string, CancellationToken, Task>? onStandardOutput,
            Func<string, CancellationToken, Task>? onStandardError,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "Resume must not start a new session.");
        }

        public async Task<CodexProcessResult> ResumeAsync(
            string workingDirectory,
            string sessionId,
            string prompt,
            Func<string, CancellationToken, Task>? onStandardOutput,
            Func<string, CancellationToken, Task>? onStandardError,
            CancellationToken cancellationToken = default)
        {
            RequestedSessionId = sessionId;
            ReceivedPrompt = prompt;

            Assert.NotNull(onStandardOutput);

            var lines = new[]
            {
            $$"""
            {"type":"thread.started","thread_id":"{{emittedSessionId}}"}
            """,
            """
            {"type":"item.completed","item":{"type":"agent_message","text":"Continued"}}
            """,
            """
            {"type":"turn.completed"}
            """
        };

            foreach (var line in lines)
            {
                await onStandardOutput(
                    line + Environment.NewLine,
                    cancellationToken);
            }

            return new CodexProcessResult(
                0,
                string.Join(Environment.NewLine, lines) +
                    Environment.NewLine,
                string.Empty);
        }
    }

    private sealed class FakeCodexProcessClient(
        Func<bool> sessionWasSaved)
        : ICodexProcessClient
    {
        public async Task<CodexProcessResult> ExecuteAsync(
            string workingDirectory,
            string prompt,
            Func<string, CancellationToken, Task>?
                onStandardOutput,
            Func<string, CancellationToken, Task>?
                onStandardError,
            CancellationToken cancellationToken = default)
        {
            Assert.NotNull(onStandardOutput);
            Assert.False(string.IsNullOrWhiteSpace(prompt));

            var lines = new[]
            {
                """
                {"type":"thread.started","thread_id":"session-1"}
                """,
                """
                {"type":"item.completed","item":{"type":"agent_message","text":"Done"}}
                """,
                """
                {"type":"turn.completed"}
                """
            };

            await onStandardOutput(
                lines[0] + Environment.NewLine,
                cancellationToken);

            // The runner must await the persistence callback before
            // the process client sends the next event.
            Assert.True(sessionWasSaved());

            await onStandardOutput(
                lines[1] + Environment.NewLine,
                cancellationToken);

            await onStandardOutput(
                lines[2] + Environment.NewLine,
                cancellationToken);

            return new CodexProcessResult(
                ExitCode: 0,
                StandardOutput:
                    string.Join(Environment.NewLine, lines) +
                    Environment.NewLine,
                StandardError: string.Empty);
        }

        public Task<CodexProcessResult> ResumeAsync(
            string workingDirectory,
            string sessionId,
            string prompt,
            Func<string, CancellationToken, Task>? onStandardOutput,
            Func<string, CancellationToken, Task>? onStandardError,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "This fake covers initial execution only.");
        }
    }
}