using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkItems;
using Xunit;

namespace Traily.AgentTeam.Tests;

public sealed class WorkItemSynchronizerTests
{
    private static readonly DateTimeOffset SourceTime =
        new(2026, 9, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RepeatedSyncCreatesOnlyOneQueuedJob()
    {
        await using var connection = await OpenConnectionAsync();
        await using var database = await CreateDatabaseAsync(connection);

        var discovery = new FakeDiscovery(ReadyItem());
        var synchronizer = new WorkItemSynchronizer(
            discovery, database);

        var first = Assert.Single(await synchronizer.SyncAsync());
        var second = Assert.Single(await synchronizer.SyncAsync());
        var job = Assert.Single(
            await database.WorkItemJobs.AsNoTracking().ToListAsync());

        Assert.Equal(WorkItemSyncOutcome.New, first.Outcome);
        Assert.Equal(WorkItemSyncOutcome.AlreadyQueued, second.Outcome);
        Assert.Equal(WorkItemJobStatus.Queued, job.Status);
        Assert.Equal("youtrack-stepin", job.SourceId);
        Assert.Equal("3-20", job.ExternalWorkItemId);
        Assert.Equal("STEPI-18", job.WorkItemReference);
        Assert.Equal("2-2", job.ExternalAssigneeId);
        Assert.Equal("team-lead", job.AgentId);
    }

    [Fact]
    public async Task NewerSourceUpdatesSnapshotButPreservesJobStatus()
    {
        await using var connection = await OpenConnectionAsync();
        await using var database = await CreateDatabaseAsync(connection);

        var discovery = new FakeDiscovery(ReadyItem());
        var synchronizer = new WorkItemSynchronizer(
            discovery, database);

        await synchronizer.SyncAsync();

        var job = await database.WorkItemJobs.SingleAsync();
        job.Status = WorkItemJobStatus.Completed;
        await database.SaveChangesAsync();

        var newerTime = SourceTime.AddMinutes(1);
        discovery.Items =
        [
            ReadyItem() with
            {
                Title = "Revised title",
                SourceUpdatedAt = newerTime
            }
        ];

        var result = Assert.Single(
            await synchronizer.SyncAsync());

        Assert.Equal(WorkItemSyncOutcome.Updated, result.Outcome);
        Assert.Equal("Revised title", job.Title);
        Assert.Equal(newerTime, job.SourceUpdatedAt);
        Assert.Equal(WorkItemJobStatus.Completed, job.Status);
        Assert.Equal(1, await database.WorkItemJobs.CountAsync());
    }

    [Fact]
    public async Task UnmappedAndInactiveAgentsDoNotCreateJobs()
    {
        await using var connection = await OpenConnectionAsync();
        await using var database = await CreateDatabaseAsync(
            connection, includeIdentity: false);

        var discovery = new FakeDiscovery(ReadyItem());
        var synchronizer = new WorkItemSynchronizer(
            discovery, database);

        var unmapped = Assert.Single(
            await synchronizer.SyncAsync());

        Assert.Equal(WorkItemSyncOutcome.Unmapped, unmapped.Outcome);

        var agent = await database.AgentProfiles.SingleAsync();
        agent.IsEnabled = false;
        database.AgentExternalIdentities.Add(
            CreateIdentity());
        await database.SaveChangesAsync();

        var inactive = Assert.Single(
            await synchronizer.SyncAsync());

        Assert.Equal(
            WorkItemSyncOutcome.AgentInactive,
            inactive.Outcome);
        Assert.Equal(0, await database.WorkItemJobs.CountAsync());
    }

    [Fact]
    public async Task ChangedExternalAssigneeDoesNotMoveExistingJob()
    {
        await using var connection = await OpenConnectionAsync();
        await using var database = await CreateDatabaseAsync(connection);

        var discovery = new FakeDiscovery(ReadyItem());
        var synchronizer = new WorkItemSynchronizer(
            discovery, database);

        await synchronizer.SyncAsync();

        database.AgentExternalIdentities.Add(
            new AgentExternalIdentity
            {
                AgentId = "team-lead",
                SourceId = "youtrack-stepin",
                ExternalUserId = "2-3",
                Login = "replacement-team-lead",
                CreatedAt = SourceTime,
                UpdatedAt = SourceTime
            });
        await database.SaveChangesAsync();

        discovery.Items =
        [
            ReadyItem() with
            {
                ExternalAssigneeId = "2-3",
                AssigneeLogin = "replacement-team-lead",
                SourceUpdatedAt = SourceTime.AddMinutes(1)
            }
        ];

        var result = Assert.Single(
            await synchronizer.SyncAsync());
        var job = await database.WorkItemJobs.SingleAsync();

        Assert.Equal(
            WorkItemSyncOutcome.AssignmentChanged,
            result.Outcome);
        Assert.Equal("2-2", job.ExternalAssigneeId);
        Assert.Equal("team-lead", job.AgentId);
        Assert.Equal(SourceTime, job.SourceUpdatedAt);
        Assert.Equal(1, await database.WorkItemJobs.CountAsync());
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=True");

        await connection.OpenAsync();
        return connection;
    }

    private static async Task<TrailyDbContext> CreateDatabaseAsync(
        SqliteConnection connection,
        bool includeIdentity = true)
    {
        var options = new DbContextOptionsBuilder<TrailyDbContext>()
            .UseSqlite(connection)
            .Options;

        var database = new TrailyDbContext(options);
        await database.Database.EnsureCreatedAsync();

        database.AgentProfiles.Add(new AgentProfile
        {
            Id = "team-lead",
            Name = "Team Lead",
            Instructions = "Analyze tasks.",
            IsEnabled = true,
            MaxConcurrentJobs = 1,
            CreatedAt = SourceTime,
            UpdatedAt = SourceTime
        });

        if (includeIdentity)
        {
            database.AgentExternalIdentities.Add(
                CreateIdentity());
        }

        await database.SaveChangesAsync();
        return database;
    }

    private static AgentExternalIdentity CreateIdentity() =>
        new()
        {
            AgentId = "team-lead",
            SourceId = "youtrack-stepin",
            ExternalUserId = "2-2",
            Login = "traily-team-lead",
            DisplayName = "Traily Team Lead",
            CreatedAt = SourceTime,
            UpdatedAt = SourceTime
        };

    private static DiscoveredWorkItem ReadyItem() =>
        new(
            SourceId: "youtrack-stepin",
            ExternalWorkItemId: "3-20",
            WorkItemReference: "STEPI-18",
            Title: "Initial domain entities",
            State: "To Do",
            ExternalAssigneeId: "2-2",
            AssigneeLogin: "traily-team-lead",
            AssigneeDisplayName: "Traily Team Lead",
            SourceUpdatedAt: SourceTime);

    private sealed class FakeDiscovery(
        params DiscoveredWorkItem[] items) : IWorkItemDiscovery
    {
        public IReadOnlyList<DiscoveredWorkItem> Items { get; set; } =
            items;

        public Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items);
    }
}