using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkSources;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Tests;

public sealed class WorkItemPlanningServiceTests
{
    [Theory]
    [InlineData("ready")]
    [InlineData("uncertain")]
    [InlineData("revoked")]
    [InlineData("invalid")]
    [InlineData("obsolete")]
    public async Task PlanningCheckpointPreservesOwnershipAndDecision(
        string scenario)
    {
        await using var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=True");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TrailyDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var database = new TrailyDbContext(options);

        // Verify migrations as well as the current EF model.
        await database.Database.MigrateAsync();

        var now = DateTimeOffset.UtcNow;
        var jobId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();

        database.AgentProfiles.Add(new AgentProfile
        {
            Id = "team-lead",
            Name = "Team Lead",
            Instructions = "Analyze tasks.",
            IsEnabled = true,
            MaxConcurrentJobs = 1,
            CreatedAt = now,
            UpdatedAt = now
        });

        database.ManagedProjects.Add(new ManagedProject
        {
            SourceId = "test-source",
            Name = "Test",
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

        foreach (var repositoryId in new[] { "api", "ui" })
        {
            database.GitRepositories.Add(new GitRepository
            {
                Id = repositoryId,
                SourceId = "test-source",
                Name = repositoryId,
                RemoteUrl = $"https://example.com/{repositoryId}.git",
                BaseBranch = "master",
                CreatedAt = now,
                UpdatedAt = now
            });

            database.AgentRepositoryAccesses.Add(
                new AgentRepositoryAccess
                {
                    AgentId = "team-lead",
                    RepositoryId = repositoryId,
                    Level = RepositoryAccessLevel.Read
                });
        }

        database.WorkItemJobs.Add(new WorkItemJob
        {
            Id = jobId,
            SourceId = "test-source",
            ExternalWorkItemId = "issue-1",
            WorkItemReference = "TEST-1",
            Title = "Test",
            ExternalAssigneeId = "user-1",
            AgentId = "team-lead",
            Status = WorkItemJobStatus.Running,
            CurrentAttemptId = attemptId,
            SourceUpdatedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        database.WorkItemExecutionAttempts.Add(
            new WorkItemExecutionAttempt
            {
                Id = attemptId,
                WorkItemJobId = jobId,
                CreatedAt = now
            });

        await database.SaveChangesAsync();

        var claim = new WorkItemClaim(
            jobId, attemptId, "team-lead");

        var service = new WorkItemPlanningService(
            database,
            new RepositoryAccessService(database),
            TimeProvider.System);

        var input = new WorkItemPlanningInput(
            "Current ticket description",
            now,
            "Inspect permitted repositories and explain the targets.",
            [
                new("api", new string('a', 40)),
                new("ui", new string('b', 40))
            ]);

        await service.BeginAsync(claim, input);

        // Observe committed input through another context.
        await using (var observer = new TrailyDbContext(options))
        {
            var saved = await observer.WorkItemExecutionAttempts
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(
                WorkItemExecutionPhase.Planning,
                saved.Phase);
            Assert.Equal(input.TaskSnapshot, saved.TaskSnapshot);
            Assert.Equal(
                input.TaskSourceUpdatedAt,
                saved.TaskSourceUpdatedAt);
            Assert.Null(saved.PlanningResultJson);
            Assert.Null(saved.PlanningCompletedAt);

            var restored = JsonSerializer
                .Deserialize<WorkItemPlanningInput>(
                    saved.PlanningInputJson!)!;

            Assert.Equal(
                input.EffectivePrompt,
                restored.EffectivePrompt);
            Assert.Equal(
                input.Repositories.ToArray(),
                restored.Repositories.ToArray());
        }

        if (scenario == "obsolete")
        {
            await database.WorkItemJobs.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    job => job.CurrentAttemptId,
                    (Guid?)Guid.NewGuid()));
        }

        if (scenario == "revoked")
        {
            await database.AgentRepositoryAccesses
                .Where(access => access.RepositoryId == "ui")
                .ExecuteDeleteAsync();
        }

        var result = new WorkItemPlanningResult(
            scenario == "uncertain"
                ? WorkItemPlanningOutcome.NeedsClarification
                : WorkItemPlanningOutcome.Ready,
            scenario == "uncertain"
                ? "The ticket does not establish which UI flow is required."
                : "The API contract and its UI consumer both need changes.",
            scenario == "uncertain"
                ? Array.Empty<PlannedRepository>()
                : new PlannedRepository[]
                {
                    new(
                        scenario == "invalid" ? "hidden" : "api",
                        "Change the API contract.",
                        "The inspected endpoint owns the response."),
                    new(
                        "ui",
                        "Update the consumer.",
                        "The inspected client consumes that response.")
                });

        if (scenario == "obsolete")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.SaveResultAsync(claim, result));
        }
        else
        {
            await service.SaveResultAsync(claim, result);
        }

        await using var reloaded = new TrailyDbContext(options);

        var job = await reloaded.WorkItemJobs
            .AsNoTracking()
            .SingleAsync();

        var attempt = await reloaded.WorkItemExecutionAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Null(attempt.FinishedAt);

        if (scenario == "obsolete")
        {
            Assert.Equal(WorkItemJobStatus.Running, job.Status);
            Assert.Equal(
                WorkItemExecutionPhase.Planning,
                attempt.Phase);
            Assert.Null(attempt.PlanningResultJson);
            Assert.Null(attempt.PlanningCompletedAt);
            Assert.Null(attempt.StopReason);
            return;
        }

        // Every saved result completes the planning checkpoint,
        // even when the job becomes Blocked.
        Assert.Equal(
            WorkItemExecutionPhase.PlanningComplete,
            attempt.Phase);
        Assert.NotNull(attempt.PlanningCompletedAt);

        var restoredResult = JsonSerializer
            .Deserialize<WorkItemPlanningResult>(
                attempt.PlanningResultJson!)!;

        Assert.Equal(result.Outcome, restoredResult.Outcome);
        Assert.Equal(result.Explanation, restoredResult.Explanation);
        Assert.Equal(
            result.SelectedRepositories.ToArray(),
            restoredResult.SelectedRepositories.ToArray());

        if (scenario == "ready")
        {
            Assert.Equal(WorkItemJobStatus.Running, job.Status);
            Assert.Null(attempt.StopReason);
        }
        else
        {
            Assert.Equal(WorkItemJobStatus.Blocked, job.Status);
            Assert.Equal(
                scenario switch
                {
                    "uncertain" => WorkItemStopReason.PlanningUncertain,
                    "revoked" => WorkItemStopReason.RepositoryAccessDenied,
                    _ => WorkItemStopReason.InvalidPlanningResult
                },
                attempt.StopReason);
        }

        // Neither ready nor blocked results can be overwritten.
        var replacement = result with
        {
            Explanation = "Replacement that must not be saved."
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SaveResultAsync(claim, replacement));

        await using var finalObserver = new TrailyDbContext(options);

        var unchanged = await finalObserver.WorkItemExecutionAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(
            attempt.PlanningResultJson,
            unchanged.PlanningResultJson);
        Assert.Equal(
            attempt.PlanningCompletedAt,
            unchanged.PlanningCompletedAt);
        Assert.Equal(attempt.Phase, unchanged.Phase);
        Assert.Equal(attempt.StopReason, unchanged.StopReason);
    }
}
