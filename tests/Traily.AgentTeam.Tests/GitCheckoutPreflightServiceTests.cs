using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkSources;

namespace Traily.AgentTeam.Tests;

public sealed class GitCheckoutPreflightServiceTests
{
    [Fact]
    public async Task ChecksGrantCheckoutRemoteAndBaseRef()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"traily-git-preflight-{Guid.NewGuid():N}");
        var checkout = Path.Combine(root, "checkout");
        var remote = Path.Combine(root, "remote.git");

        Directory.CreateDirectory(root);

        try
        {
            await GitAsync(root, "init", "--bare", remote);
            await GitAsync(root, "init", "-b", "master", checkout);

            File.WriteAllText(
                Path.Combine(checkout, "README.md"),
                "Temporary preflight test repository.");

            await GitAsync(checkout, "config", "user.name", "Traily Test");
            await GitAsync(
                checkout, "config", "user.email",
                "traily-test@example.invalid");
            await GitAsync(checkout, "add", "README.md");
            await GitAsync(
                checkout, "-c", "commit.gpgsign=false",
                "commit", "-m", "Initial commit");
            await GitAsync(
                checkout, "remote", "add", "origin", remote);
            await GitAsync(checkout, "push", "-u", "origin", "master");

            var expectedCommit = await GitAsync(
                checkout,
                "rev-parse",
                "refs/remotes/origin/master");

            await using var connection = new SqliteConnection(
                "Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<TrailyDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var database = new TrailyDbContext(options);
            await database.Database.EnsureCreatedAsync();

            var now = DateTimeOffset.UtcNow;

            database.ManagedProjects.Add(new ManagedProject
            {
                SourceId = "youtrack-stepin",
                Name = "StepIn",
                WorkSourceConnectionId = "test-youtrack",
                ExternalProjectId = "0-1",
                WorkSourceConnection = new WorkSourceConnection
                {
                    Id = "test-youtrack",
                    ProviderId = "YouTrack",
                    BaseUrl = "https://example.invalid",
                    CreatedAt = now,
                    UpdatedAt = now
                },
                CreatedAt = now,
                UpdatedAt = now
            });

            var repository = new GitRepository
            {
                Id = "stepin-api",
                SourceId = "youtrack-stepin",
                Name = "StepIn API",
                RemoteUrl = remote,
                BaseBranch = "master",
                LocalPathOverride = checkout,
                CreatedAt = now,
                UpdatedAt = now
            };

            database.GitRepositories.Add(repository);

            database.AgentProfiles.AddRange(
                new AgentProfile
                {
                    Id = "team-lead",
                    Name = "Team Lead",
                    Instructions = "Analyze tasks.",
                    IsEnabled = true,
                    MaxConcurrentJobs = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                new AgentProfile
                {
                    Id = "ungranted-agent",
                    Name = "Ungranted Agent",
                    Instructions = "Analyze tasks.",
                    IsEnabled = true,
                    MaxConcurrentJobs = 1,
                    CreatedAt = now,
                    UpdatedAt = now
                });

            database.AgentRepositoryAccesses.Add(
                new AgentRepositoryAccess
                {
                    AgentId = "team-lead",
                    RepositoryId = "stepin-api",
                    Level = RepositoryAccessLevel.Read
                });

            await database.SaveChangesAsync();

            var service = new GitCheckoutPreflightService(
                new RepositoryAccessService(database));

            var ready = await service.CheckAsync(
                "youtrack-stepin", "team-lead", "stepin-api");

            Assert.Equal(GitCheckoutStatus.Ready, ready.Status);
            Assert.Equal(checkout, ready.CheckoutPath);
            Assert.Equal(expectedCommit, ready.BaseCommit);

            var denied = await service.CheckAsync(
                "youtrack-stepin",
                "ungranted-agent",
                "stepin-api");

            Assert.Equal(
                GitCheckoutStatus.AccessDenied,
                denied.Status);

            repository.RemoteUrl =
                "https://example.invalid/different.git";
            await database.SaveChangesAsync();

            var wrongRemote = await service.CheckAsync(
                "youtrack-stepin", "team-lead", "stepin-api");

            Assert.Equal(
                GitCheckoutStatus.RemoteMismatch,
                wrongRemote.Status);

            repository.RemoteUrl = remote;
            repository.BaseBranch = "nonexistent";
            await database.SaveChangesAsync();

            var missingBase = await service.CheckAsync(
                "youtrack-stepin", "team-lead", "stepin-api");

            Assert.Equal(
                GitCheckoutStatus.BaseRefMissing,
                missingBase.Status);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.EnumerateFiles(
                    root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static async Task<string> GitAsync(
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        Assert.True(process.Start());

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        Assert.True(
            process.ExitCode == 0,
            $"git {string.Join(' ', arguments)} failed: {error}");

        return output.Trim();
    }
}
