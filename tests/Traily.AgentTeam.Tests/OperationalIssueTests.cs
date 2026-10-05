using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Traily.AgentTeam.Integrations.YouTrack;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Tests;

public sealed class OperationalIssueTests
{
    [Theory]
    [InlineData(404, "DiscoveryEndpointUnavailable", OperationalAvailability.Unavailable)]
    [InlineData(429, "RateLimited", OperationalAvailability.Unavailable)]
    [InlineData(503, "RemoteServiceFailure", OperationalAvailability.Unavailable)]
    [InlineData(400, "RequestRejected", OperationalAvailability.Unavailable)]
    [InlineData(null, "ConnectionFailed", OperationalAvailability.Unknown)]
    public async Task SharedHttpClassificationPreservesDiscoveryCapabilityMeaning(
        int? status, string reason, OperationalAvailability availability)
    {
        await using var fixture = await Fixture.CreateAsync();
        var discovery = fixture.CreateDiscovery(new FakeDiscovery
        {
            Failure = new HttpRequestException("private-provider-diagnostic", null,
                status is { } value ? (HttpStatusCode)value : null)
        });
        await Assert.ThrowsAsync<YouTrackDiscoveryException>(() => discovery.FindReadyAsync());
        var issue = Assert.Single(await fixture.ReadIssuesAsync());
        Assert.Equal(reason, issue.ReasonCode);
        Assert.Equal(availability, issue.Availability);
        Assert.Equal(status, issue.HttpStatusCode);
        Assert.DoesNotContain("private-provider-diagnostic", issue.Message);
    }

    [Fact]
    public async Task DiscoveryTracksFailureRecoveryAndRecurrence()
    {
        await using var fixture = await Fixture.CreateAsync();

        var inner = new FakeDiscovery
        {
            Failure = new HttpRequestException(
                "Raw diagnostic must not be persisted.",
                null,
                HttpStatusCode.Unauthorized)
        };

        var discovery = fixture.CreateDiscovery(inner);

        await Assert.ThrowsAsync<YouTrackDiscoveryException>(
            () => discovery.FindReadyAsync());

        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(1);

        await Assert.ThrowsAsync<YouTrackDiscoveryException>(
            () => discovery.FindReadyAsync());

        var first = Assert.Single(await fixture.ReadIssuesAsync());

        Assert.Equal("AuthenticationFailed", first.ReasonCode);
        Assert.Equal(401, first.HttpStatusCode);
        Assert.Equal(2L, first.ObservationCount);
        Assert.True(first.LastObservedAt > first.FirstObservedAt);
        Assert.Null(first.ResolvedAt);
        Assert.DoesNotContain("Raw diagnostic", first.Message);

        inner.Failure = null;
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(1);

        // Empty discovery is still a successful discovery request.
        Assert.Empty(await discovery.FindReadyAsync());

        var resolved = Assert.Single(await fixture.ReadIssuesAsync());

        Assert.Equal(first.Id, resolved.Id);
        Assert.Equal(fixture.Clock.Now, resolved.ResolvedAt);
        Assert.Equal(first.LastObservedAt, resolved.LastObservedAt);

        inner.Failure = new HttpRequestException(
            "Forbidden.",
            null,
            HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<YouTrackDiscoveryException>(
            () => discovery.FindReadyAsync());

        var history = await fixture.ReadIssuesAsync();

        Assert.Equal(2, history.Count);
        Assert.Single(history, issue => issue.ResolvedAt != null);

        var current = Assert.Single(history, issue => issue.ResolvedAt == null);

        Assert.NotEqual(first.Id, current.Id);
        Assert.Equal("AccessDenied", current.ReasonCode);
        Assert.Equal(1L, current.ObservationCount);
    }

    [Fact]
    public async Task ReasonChangesUpdateOnlyTheSameCapability()
    {
        await using var fixture = await Fixture.CreateAsync();

        var discovery = new OperationalScope(
            "WorkSource", "test-source", "DiscoverWorkItems");

        var writeback = discovery with
        {
            Capability = "UpdateWorkItems"
        };

        await fixture.Issues.ObserveAsync(new(
            discovery,
            OperationalAvailability.Unknown,
            "ConnectionFailed",
            "Connection could not be established."));

        await fixture.Issues.ObserveAsync(new(
            writeback,
            OperationalAvailability.Unavailable,
            "AccessDenied",
            "Ticket updates were denied."));

        var transition = await fixture.Issues.ObserveAsync(new(
            discovery,
            OperationalAvailability.Unavailable,
            "AuthenticationFailed",
            "Credentials were rejected.",
            401));

        Assert.Equal(
            OperationalIssueTransition.Changed,
            transition);

        await fixture.Issues.ObserveAsync(new(
            discovery,
            OperationalAvailability.Available));

        var history = await fixture.ReadIssuesAsync();

        Assert.Equal(2, history.Count);

        var open = Assert.Single(history, issue => issue.ResolvedAt == null);

        Assert.Equal("UpdateWorkItems", open.Capability);

        var closed = Assert.Single(history, issue => issue.ResolvedAt != null);

        Assert.Equal(2L, closed.ObservationCount);
        Assert.Equal("AuthenticationFailed", closed.ReasonCode);
    }

    [Fact]
    public async Task ShutdownCancellationCreatesNoIssue()
    {
        await using var fixture = await Fixture.CreateAsync();

        var inner = new FakeDiscovery();
        var discovery = fixture.CreateDiscovery(inner);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => discovery.FindReadyAsync(cancellation.Token));

        Assert.Empty(await fixture.ReadIssuesAsync());
    }

    [Fact]
    public async Task QueueFailureDoesNotKeepDiscoveryIssueOpen()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Issues.ObserveAsync(new(
            new("WorkSource", "test-source", "DiscoverWorkItems"),
            OperationalAvailability.Unavailable,
            "AuthenticationFailed",
            "Credentials were rejected.",
            401));

        var discovery = fixture.CreateDiscovery(new FakeDiscovery());

        // Discovery and empty synchronization succeed;
        // the subsequent queued-job count fails independently.
        using var brokenConnection = new SqliteConnection(
            "Data Source=:memory:");

        await brokenConnection.OpenAsync();

        var brokenOptions =
            new DbContextOptionsBuilder<TrailyDbContext>()
                .UseSqlite(brokenConnection)
                .Options;

        await using var brokenDatabase =
            new TrailyDbContext(brokenOptions);

        var synchronizer = new WorkItemSynchronizer(
            discovery,
            brokenDatabase);

        await synchronizer.SyncAsync();

        await Assert.ThrowsAsync<SqliteException>(
            () => brokenDatabase.WorkItemJobs.CountAsync());

        var issue = Assert.Single(await fixture.ReadIssuesAsync());
        Assert.NotNull(issue.ResolvedAt);
    }

    [Fact]
    public async Task IssueStoreFailurePreservesSuccessfulDiscovery()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RemoveIssueTableAsync();

        var discovery = fixture.CreateDiscovery(new FakeDiscovery());

        Assert.Empty(await discovery.FindReadyAsync());
    }

    [Fact]
    public async Task IssueStoreFailurePreservesOriginalDiscoveryFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.RemoveIssueTableAsync();

        var original = new HttpRequestException(
            "Authentication failed.",
            null,
            HttpStatusCode.Unauthorized);

        var discovery = fixture.CreateDiscovery(
            new FakeDiscovery { Failure = original });

        var exception =
            await Assert.ThrowsAsync<YouTrackDiscoveryException>(
                () => discovery.FindReadyAsync());

        Assert.Equal("AuthenticationFailed", exception.ReasonCode);
        Assert.Same(original, exception.InnerException);
    }

    private sealed class FakeDiscovery : IWorkItemDiscovery
    {
        public Exception? Failure { get; set; }

        public Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Failure is { } failure
                ? Task.FromException<
                    IReadOnlyList<DiscoveredWorkItem>>(failure)
                : Task.FromResult<
                    IReadOnlyList<DiscoveredWorkItem>>(
                        Array.Empty<DiscoveredWorkItem>());
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } =
            new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture(
        SqliteConnection connection,
        ServiceProvider provider,
        TestClock clock)
        : IAsyncDisposable
    {
        public TestClock Clock { get; } = clock;

        public OperationalIssueService Issues =>
            provider.GetRequiredService<OperationalIssueService>();

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection(
                "Data Source=:memory:;Foreign Keys=True");

            await connection.OpenAsync();

            var clock = new TestClock();
            var services = new ServiceCollection();

            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddDbContext<TrailyDbContext>(
                options => options.UseSqlite(connection));
            services.AddSingleton<OperationalIssueService>();

            var provider = services.BuildServiceProvider();

            await using var scope = provider.CreateAsyncScope();

            var database = scope.ServiceProvider
                .GetRequiredService<TrailyDbContext>();

            await database.Database.MigrateAsync();

            return new Fixture(connection, provider, clock);
        }

        public ObservedYouTrackDiscovery CreateDiscovery(
            IWorkItemDiscovery inner) =>
            new(
                inner,
                "test-source",
                Issues,
                NullLogger<ObservedYouTrackDiscovery>.Instance);

        public async Task<List<OperationalIssue>> ReadIssuesAsync()
        {
            await using var scope = provider.CreateAsyncScope();

            return await scope.ServiceProvider
                .GetRequiredService<TrailyDbContext>()
                .OperationalIssues
                .AsNoTracking()
                .ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }

        public async Task RemoveIssueTableAsync()
        {
            await using var scope = provider.CreateAsyncScope();

            var database = scope.ServiceProvider
                .GetRequiredService<TrailyDbContext>();

            await database.Database.ExecuteSqlRawAsync(
                "DROP TABLE OperationalIssues");
        }
    }
}
