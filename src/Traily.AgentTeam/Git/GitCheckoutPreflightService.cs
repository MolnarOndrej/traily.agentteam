using System.Diagnostics;

namespace Traily.AgentTeam.Git;

public enum GitCheckoutStatus
{
    Ready,
    AccessDenied,
    CheckoutNotConfigured,
    CheckoutMissing,
    InvalidCheckout,
    RemoteMismatch,
    BaseRefMissing
}

public sealed record GitCheckoutResult(
    GitCheckoutStatus Status,
    string? CheckoutPath = null,
    string? BaseCommit = null);

public sealed class GitCheckoutPreflightService(
    RepositoryAccessService accessService)
{
    public async Task<GitCheckoutResult> CheckAsync(
        string sourceId,
        string agentId,
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);

        var repositories =
            await accessService.GetAccessibleRepositoriesAsync(
                sourceId, agentId, cancellationToken);

        var repository = repositories.FirstOrDefault(entry =>
            string.Equals(
                entry.RepositoryId,
                repositoryId,
                StringComparison.Ordinal));

        if (repository is null)
        {
            return new(GitCheckoutStatus.AccessDenied);
        }

        if (string.IsNullOrWhiteSpace(
            repository.LocalPathOverride))
        {
            // Managed cloning from the Traily data root comes later.
            return new(GitCheckoutStatus.CheckoutNotConfigured);
        }

        var path = Path.GetFullPath(
            repository.LocalPathOverride);

        if (!Directory.Exists(path))
        {
            return new(GitCheckoutStatus.CheckoutMissing);
        }

        var root = await GitAsync(
            path, cancellationToken,
            "rev-parse", "--show-toplevel");

        if (root.ExitCode != 0)
        {
            return new(GitCheckoutStatus.InvalidCheckout);
        }

        var actualRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root.Output.Trim()));
        var expectedRoot = Path.TrimEndingDirectorySeparator(path);

        var pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(
            actualRoot, expectedRoot, pathComparison))
        {
            return new(GitCheckoutStatus.InvalidCheckout);
        }

        var remote = await GitAsync(
            path, cancellationToken,
            "remote", "get-url", "origin");

        if (remote.ExitCode != 0 ||
            !string.Equals(
                remote.Output.Trim(),
                repository.RemoteUrl,
                StringComparison.Ordinal))
        {
            return new(GitCheckoutStatus.RemoteMismatch);
        }

        var baseCommit = await GitAsync(
            path, cancellationToken,
            "rev-parse", "--verify",
            $"refs/remotes/origin/{repository.BaseBranch}^{{commit}}");

        if (baseCommit.ExitCode != 0)
        {
            return new(GitCheckoutStatus.BaseRefMissing);
        }

        return new(
            GitCheckoutStatus.Ready,
            path,
            baseCommit.Output.Trim());
    }

    private static async Task<GitCommandResult> GitAsync(
        string workingDirectory,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        cancellationToken.ThrowIfCancellationRequested();

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Failed to start Git.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
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
            await Task.WhenAll(outputTask, errorTask);
            throw;
        }

        return new GitCommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private sealed record GitCommandResult(
        int ExitCode,
        string Output,
        string Error);
}