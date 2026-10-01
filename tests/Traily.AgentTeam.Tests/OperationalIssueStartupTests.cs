using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Hosting;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Tests;

public sealed class OperationalIssueStartupTests
{
    [Fact]
    public async Task ReportsOnlyUnresolvedIssuesWithoutChangingHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var open = CreateIssue("open", OperationalAvailability.Unknown);
        var resolved = CreateIssue("resolved", OperationalAvailability.Unavailable);
        resolved.ResolvedAt = resolved.LastObservedAt.AddMinutes(1);
        await fixture.SeedAsync(open, resolved);
        var before = await fixture.ReadHistoryAsync();

        await fixture.Reporter.StartAsync(CancellationToken.None);

        var warning = Assert.Single(fixture.Logger.Entries,
            entry => entry.Level == LogLevel.Warning);
        Assert.Contains(open.Id.ToString(), warning.Message);
        Assert.Contains("WorkSource open; capability DiscoverWorkItems", warning.Message);
        Assert.Contains("Unknown; ConnectionFailed", warning.Message);
        Assert.Contains("last observed", warning.Message);
        Assert.Contains("Awaiting revalidation", warning.Message);
        Assert.DoesNotContain(resolved.Id.ToString(), warning.Message);
        Assert.Equal(open.LastObservedAt, warning.Properties["LastObservedAt"]);
        Assert.Equal(before, await fixture.ReadHistoryAsync());
    }

    [Fact]
    public async Task EmptyStoreReportsZeroWithoutWarnings()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.Reporter.StartAsync(CancellationToken.None);

        var entry = Assert.Single(fixture.Logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(0, entry.Properties["UnresolvedIssueCount"]);
        Assert.Contains("not been revalidated", entry.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupReportsBeforeDiscoveryAndStoreFailureAllowsPolling(bool brokenStore)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(CreateIssue("open", OperationalAvailability.Unavailable));
        if (brokenStore)
        {
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TrailyDbContext>()
                .Database.ExecuteSqlRawAsync("DROP TABLE OperationalIssues");
        }

        await fixture.Host.StartAsync();
        var entriesAtDiscovery = await fixture.Discovery.FirstPoll.Task
            .WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Host.StopAsync();

        Assert.Contains(entriesAtDiscovery, entry => entry.Level ==
            (brokenStore ? LogLevel.Error : LogLevel.Warning));
        if (brokenStore)
        {
            var error = Assert.Single(entriesAtDiscovery);
            Assert.Contains("status is unknown", error.Message);
            Assert.Contains("discovery polling will continue", error.Message);
            Assert.DoesNotContain("DROP TABLE", error.Message);
            Assert.DoesNotContain(entriesAtDiscovery, entry => entry.Level == LogLevel.Information);
        }
    }

    [Fact]
    public async Task StartupCancellationPropagatesWithoutErrorOrHistoryChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(CreateIssue("open", OperationalAvailability.Unknown));
        var before = await fixture.ReadHistoryAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Reporter.StartAsync(cancellation.Token));

        Assert.Empty(fixture.Logger.Entries);
        Assert.Equal(before, await fixture.ReadHistoryAsync());
    }

    private static OperationalIssue CreateIssue(string scopeId, OperationalAvailability availability)
        => new()
        {
            Id = Guid.NewGuid(),
            ScopeType = "WorkSource",
            ScopeId = scopeId,
            Capability = "DiscoverWorkItems",
            Availability = availability,
            ReasonCode = "ConnectionFailed",
            Message = "The discovery request could not reach YouTrack.",
            FirstObservedAt = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero),
            LastObservedAt = new(2026, 10, 1, 10, 5, 0, TimeSpan.Zero),
            ObservationCount = 3
        };

    private sealed record LogEntry(
        LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties);

    private sealed class CapturingLogger : ILogger<OperationalIssueStartupReporter>
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = ((IEnumerable<KeyValuePair<string, object?>>)state!)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Entries.Enqueue(new(logLevel, formatter(state, exception), properties));
        }
    }

    private sealed class RecordingDiscovery(CapturingLogger logger) : IWorkItemDiscovery
    {
        public TaskCompletionSource<LogEntry[]> FirstPoll { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(
            CancellationToken cancellationToken = default)
        {
            FirstPoll.TrySetResult(logger.Entries.ToArray());
            return Task.FromResult<IReadOnlyList<DiscoveredWorkItem>>([]);
        }
    }

    private sealed class Fixture(SqliteConnection connection, IHost host,
        CapturingLogger logger, RecordingDiscovery discovery) : IAsyncDisposable
    {
        public IHost Host { get; } = host;
        public CapturingLogger Logger { get; } = logger;
        public RecordingDiscovery Discovery { get; } = discovery;
        public OperationalIssueStartupReporter Reporter =>
            Host.Services.GetServices<IHostedService>()
                .OfType<OperationalIssueStartupReporter>().Single();

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var logger = new CapturingLogger();
            var discovery = new RecordingDiscovery(logger);
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<ILogger<OperationalIssueStartupReporter>>(logger);
            builder.Services.AddDbContext<TrailyDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddScoped<OperationalIssueQuery>();
            builder.Services.AddHostedService<OperationalIssueStartupReporter>();
            builder.Services.AddHostedService<WorkItemPollingService>();
            builder.Services.AddSingleton(new WorkItemPollingConfiguration(TimeSpan.FromMinutes(1)));
            builder.Services.AddSingleton<IWorkItemDiscovery>(discovery);
            builder.Services.AddScoped<WorkItemSynchronizer>();
            var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TrailyDbContext>().Database.MigrateAsync();
            return new(connection, host, logger, discovery);
        }

        public async Task SeedAsync(params OperationalIssue[] issues)
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TrailyDbContext>();
            database.OperationalIssues.AddRange(issues);
            await database.SaveChangesAsync();
        }

        public async Task<string> ReadHistoryAsync()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TrailyDbContext>();
            return JsonSerializer.Serialize(await database.OperationalIssues
                .AsNoTracking().OrderBy(issue => issue.ScopeId).ToListAsync());
        }

        public async ValueTask DisposeAsync()
        {
            Host.Dispose();
            await connection.DisposeAsync();
        }
    }
}
