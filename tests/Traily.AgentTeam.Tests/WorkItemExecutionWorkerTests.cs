using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Tests;

public sealed class WorkItemExecutionWorkerTests
{
    private static readonly DateTimeOffset TicketUpdatedAt =
        new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ChangedAssigneeBlocksWithoutCallingProvider()
    {
        await using var connection = await OpenConnectionAsync();
        var options = CreateOptions(connection);
        await SeedAsync(options);

        await using var database = new TrailyDbContext(options);

        var provider = new FakeProvider((_, _, _) =>
            Task.FromResult(
                new WorkItemExecutionResult(true, null)));

        var worker = CreateWorker(
            database,
            new FakeReader(CreateTicket(
                assigneeId: "another-user")),
            provider);

        Assert.True(await worker.RunNextAsync(
            Path.GetTempPath(),
            "To Do"));

        Assert.Equal(0, provider.Calls);

        var job = await database.WorkItemJobs
            .AsNoTracking()
            .SingleAsync();

        var attempt = await database.WorkItemExecutionAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(WorkItemJobStatus.Blocked, job.Status);
        Assert.Equal(
            WorkItemStopReason.TicketAssignmentChanged,
            attempt.StopReason);
        Assert.Null(attempt.TaskSnapshot);
    }

    [Fact]
    public async Task MatchingTicketSavesSnapshotBeforeProviderAndCompletes()
    {
        await using var connection = await OpenConnectionAsync();
        var options = CreateOptions(connection);
        await SeedAsync(options);

        await using var database = new TrailyDbContext(options);

        var provider = new FakeProvider(
            async (request, onSessionAvailable, token) =>
            {
                // A different context verifies that the snapshot was
                // committed before the provider was invoked.
                await using var reader =
                    new TrailyDbContext(options);

                var saved = await reader.WorkItemExecutionAttempts
                    .AsNoTracking()
                    .SingleAsync(token);

                Assert.Equal(
                    request.TaskSnapshot,
                    saved.TaskSnapshot);
                Assert.Equal(
                    TicketUpdatedAt,
                    saved.TaskSourceUpdatedAt);
                Assert.Equal(
                    "fake-provider",
                    saved.ProviderId);
                Assert.NotNull(saved.StartedAt);

                await onSessionAvailable(
                    "session-1",
                    token);

                return new WorkItemExecutionResult(
                    true,
                    null);
            });

        var worker = CreateWorker(
            database,
            new FakeReader(CreateTicket()),
            provider);

        Assert.True(await worker.RunNextAsync(
            Path.GetTempPath(),
            "To Do"));

        var job = await database.WorkItemJobs
            .AsNoTracking()
            .SingleAsync();

        var attempt = await database.WorkItemExecutionAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(1, provider.Calls);
        Assert.Equal(
            WorkItemJobStatus.AwaitingReview,
            job.Status);
        Assert.Equal(
            "session-1",
            attempt.ProviderSessionId);
        Assert.Contains(
            "Current description",
            attempt.TaskSnapshot);
        Assert.NotNull(attempt.FinishedAt);
        Assert.Null(attempt.StopReason);
    }

    [Fact]
    public async Task ProviderStopRetainsSessionAndSnapshot()
    {
        await using var connection = await OpenConnectionAsync();
        var options = CreateOptions(connection);
        await SeedAsync(options);

        await using var database = new TrailyDbContext(options);

        var provider = new FakeProvider(
            async (_, onSessionAvailable, token) =>
            {
                await onSessionAvailable(
                    "session-2",
                    token);

                return new WorkItemExecutionResult(
                    false,
                    WorkItemStopReason.UsageLimitReached);
            });

        var worker = CreateWorker(
            database,
            new FakeReader(CreateTicket()),
            provider);

        Assert.True(await worker.RunNextAsync(
            Path.GetTempPath(),
            "To Do"));

        var job = await database.WorkItemJobs
            .AsNoTracking()
            .SingleAsync();

        var attempt = await database.WorkItemExecutionAttempts
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(WorkItemJobStatus.Blocked, job.Status);
        Assert.Equal(
            WorkItemStopReason.UsageLimitReached,
            attempt.StopReason);
        Assert.Equal(
            "session-2",
            attempt.ProviderSessionId);
        Assert.NotNull(attempt.TaskSnapshot);
        Assert.NotNull(attempt.WorkingDirectory);
        Assert.Null(attempt.FinishedAt);
    }

    [Fact]
    public async Task CancellationLeavesJobRunningForRecovery()
    {
        await using var connection = await OpenConnectionAsync();
        var options = CreateOptions(connection);
        await SeedAsync(options);

        await using var database = new TrailyDbContext(options);
        using var cancellation = new CancellationTokenSource();

        var provider = new FakeProvider(
            (_, _, token) =>
            {
                cancellation.Cancel();

                return Task.FromCanceled<WorkItemExecutionResult>(
                    token);
            });

        var worker = CreateWorker(
            database,
            new FakeReader(CreateTicket()),
            provider);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => worker.RunNextAsync(
                Path.GetTempPath(),
                "To Do",
                cancellation.Token));

        var job = await database.WorkItemJobs
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(WorkItemJobStatus.Running, job.Status);
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("session")]
    [InlineData("finish")]
    public async Task ChangedJobAgentRejectsWritesFromTheOriginalClaim(string stage)
    {
        await using var connection = await OpenConnectionAsync();
        var options = CreateOptions(connection);
        await SeedAsync(options);
        await using var database = new TrailyDbContext(options);

        database.AgentProfiles.Add(new AgentProfile
        {
            Id = "another-agent",
            Name = "Another Agent",
            Instructions = "Analyze tasks.",
            IsEnabled = true,
            MaxConcurrentJobs = 1,
            CreatedAt = TicketUpdatedAt,
            UpdatedAt = TicketUpdatedAt
        });
        await database.SaveChangesAsync();

        async Task ChangeJobAgentAsync()
        {
            await database.WorkItemJobs.ExecuteUpdateAsync(setters =>
                setters.SetProperty(job => job.AgentId, "another-agent"));
        }

        var provider = new FakeProvider(async (_, onSessionAvailable, token) =>
        {
            if (stage == "session")
            {
                await onSessionAvailable("original-session", token);
            }

            await ChangeJobAgentAsync();

            if (stage == "session")
            {
                await onSessionAvailable("rejected-session", token);
            }

            return new WorkItemExecutionResult(true, null);
        });
        var reader = new FakeReader(CreateTicket(),
            stage == "prepare" ? ChangeJobAgentAsync : null);
        var worker = CreateWorker(database, reader, provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            worker.RunNextAsync(Path.GetTempPath(), "To Do"));

        // Reload through a separate context to verify committed state rather
        // than the worker context's tracked entities.
        await using var observer = new TrailyDbContext(options);
        var job = await observer.WorkItemJobs.AsNoTracking().SingleAsync();
        var attempt = await observer.WorkItemExecutionAttempts.AsNoTracking().SingleAsync();
        Assert.Equal("another-agent", job.AgentId);
        Assert.Equal(WorkItemJobStatus.Running, job.Status);
        Assert.Equal(attempt.Id, job.CurrentAttemptId);
        Assert.Null(attempt.StopReason);
        Assert.Null(attempt.FinishedAt);
        Assert.Equal(stage == "session" ? "original-session" : null, attempt.ProviderSessionId);

        if (stage == "prepare")
        {
            Assert.Equal(0, provider.Calls);
            Assert.Null(attempt.TaskSnapshot);
            Assert.Null(attempt.TaskSourceUpdatedAt);
            Assert.Null(attempt.WorkingDirectory);
            Assert.Null(attempt.ProviderId);
            Assert.Null(attempt.StartedAt);
        }
        else
        {
            Assert.Equal(1, provider.Calls);
            Assert.Contains("Current description", attempt.TaskSnapshot);
            Assert.Equal(TicketUpdatedAt, attempt.TaskSourceUpdatedAt);
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()), attempt.WorkingDirectory);
            Assert.Equal("fake-provider", attempt.ProviderId);
            Assert.NotNull(attempt.StartedAt);
        }
    }

    private static WorkItemExecutionWorker CreateWorker(
        TrailyDbContext database,
        IWorkItemReader reader,
        IWorkItemExecutionProvider provider)
    {
        return new WorkItemExecutionWorker(
            new WorkItemClaimService(
                database,
                TimeProvider.System),
            database,
            reader,
            provider,
            TimeProvider.System);
    }

    private static WorkItem CreateTicket(
        string assigneeId = "user-1")
    {
        return new WorkItem(
            "issue-1",
            "TEST-1",
            "Current title",
            "Current description",
            "To Do",
            assigneeId,
            TicketUpdatedAt);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(
            "Data Source=:memory:");

        await connection.OpenAsync();
        return connection;
    }

    private static DbContextOptions<TrailyDbContext> CreateOptions(
        SqliteConnection connection)
    {
        return new DbContextOptionsBuilder<TrailyDbContext>()
            .UseSqlite(connection)
            .Options;
    }

    private static async Task SeedAsync(
        DbContextOptions<TrailyDbContext> options)
    {
        await using var database =
            new TrailyDbContext(options);

        await database.Database.EnsureCreatedAsync();

        database.AgentProfiles.Add(new AgentProfile
        {
            Id = "team-lead",
            Name = "Team Lead",
            Instructions = "Analyze tasks.",
            IsEnabled = true,
            MaxConcurrentJobs = 1,
            CreatedAt = TicketUpdatedAt,
            UpdatedAt = TicketUpdatedAt
        });

        database.WorkItemJobs.Add(new WorkItemJob
        {
            Id = Guid.NewGuid(),
            SourceId = "test-source",
            ExternalWorkItemId = "issue-1",
            WorkItemReference = "TEST-1",
            Title = "Queued title",
            ExternalAssigneeId = "user-1",
            AgentId = "team-lead",
            Status = WorkItemJobStatus.Queued,
            SourceUpdatedAt = TicketUpdatedAt.AddMinutes(-5),
            CreatedAt = TicketUpdatedAt,
            UpdatedAt = TicketUpdatedAt
        });

        await database.SaveChangesAsync();
    }

    private sealed class FakeReader(WorkItem ticket, Func<Task>? onRead = null)
        : IWorkItemReader
    {
        public async Task<WorkItem> GetRequiredAsync(
            string sourceId,
            string externalWorkItemId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal("test-source", sourceId);
            Assert.Equal("issue-1", externalWorkItemId);

            if (onRead is not null)
            {
                await onRead();
            }

            return ticket;
        }
    }

    private sealed class FakeProvider(
        Func<
            WorkItemExecutionRequest,
            Func<string, CancellationToken, Task>,
            CancellationToken,
            Task<WorkItemExecutionResult>> run)
        : IWorkItemExecutionProvider
    {
        public string ProviderId => "fake-provider";

        public int Calls { get; private set; }

        public Task<WorkItemExecutionResult> RunAsync(
            WorkItemExecutionRequest request,
            Func<string, CancellationToken, Task>
                onSessionAvailable,
            CancellationToken cancellationToken)
        {
            Calls++;

            return run(
                request,
                onSessionAvailable,
                cancellationToken);
        }
    }
}
