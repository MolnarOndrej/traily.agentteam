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

public sealed class YouTrackConfigurationStartupTests
{
    [Theory]
    [InlineData(YouTrackConfiguration.BaseUrlEnvironmentVariable)]
    [InlineData(YouTrackConfiguration.TokenEnvironmentVariable)]
    [InlineData(YouTrackConfiguration.DiscoveryQueryEnvironmentVariable)]
    [InlineData(YouTrackConfiguration.SourceIdEnvironmentVariable)]
    public void MissingRequiredSettingProducesSafeDiagnostic(string setting)
    {
        var values = ValidSettings();
        values[setting] = "  ";

        var exception = Assert.Throws<YouTrackConfigurationException>(
            () => YouTrackConfiguration.FromEnvironment(values.GetValueOrDefault));

        Assert.Equal($"Configure {setting}.", exception.Message);
    }

    [Theory]
    [InlineData("http://private-url-marker.example/")]
    [InlineData("not-a-url-private-marker")]
    public void InvalidUrlDoesNotExposeAnyConfiguredValues(string url)
    {
        var values = ValidSettings();
        values[YouTrackConfiguration.BaseUrlEnvironmentVariable] = url;

        var exception = Assert.Throws<YouTrackConfigurationException>(
            () => YouTrackConfiguration.FromEnvironment(values.GetValueOrDefault));

        Assert.Equal("TRAILY_YOUTRACK_BASE_URL must be an HTTPS URL.", exception.Message);
        Assert.DoesNotContain(url, exception.ToString());
        Assert.DoesNotContain("secret-token-marker", exception.ToString());
        Assert.DoesNotContain("private-query-marker", exception.ToString());
    }

    [Theory]
    [InlineData(100)]
    [InlineData(101)]
    public void SourceIdLengthMatchesPersistedSourceLimit(int length)
    {
        var values = ValidSettings();
        values[YouTrackConfiguration.SourceIdEnvironmentVariable] = new string('s', length);
        if (length == 100)
        {
            Assert.Equal(length, YouTrackConfiguration.FromEnvironment(
                values.GetValueOrDefault).SourceId.Length);
        }
        else
        {
            var exception = Assert.Throws<YouTrackConfigurationException>(
                () => YouTrackConfiguration.FromEnvironment(values.GetValueOrDefault));
            Assert.Equal("TRAILY_YOUTRACK_SOURCE_ID must not exceed 100 characters.", exception.Message);
        }
    }

    [Fact]
    public void ValidSettingsAreNormalizedWithOptionalDefaults()
    {
        var values = ValidSettings();
        values[YouTrackConfiguration.BaseUrlEnvironmentVariable] = " https://example.invalid/// ";
        values[YouTrackConfiguration.SourceIdEnvironmentVariable] = " test-source ";
        values[YouTrackConfiguration.WorkflowStateFieldEnvironmentVariable] = "  ";

        var configuration = YouTrackConfiguration.FromEnvironment(values.GetValueOrDefault);

        Assert.Equal("https://example.invalid/", configuration.BaseAddress.AbsoluteUri);
        Assert.Equal("secret-token-marker", configuration.AccessToken);
        Assert.Equal("private-query-marker", configuration.DiscoveryQuery);
        Assert.Equal("test-source", configuration.SourceId);
        Assert.Equal("Stage", configuration.WorkflowStateField);
        Assert.Equal("Assignee", configuration.AssigneeField);

        values[YouTrackConfiguration.WorkflowStateFieldEnvironmentVariable] = " CustomStage ";
        values[YouTrackConfiguration.AssigneeFieldEnvironmentVariable] = " CustomAssignee ";
        configuration = YouTrackConfiguration.FromEnvironment(values.GetValueOrDefault);
        Assert.Equal("CustomStage", configuration.WorkflowStateField);
        Assert.Equal("CustomAssignee", configuration.AssigneeField);
    }

    [Fact]
    public async Task MissingSettingsAreReportedAfterRetainedIssuesAndPreventPolling()
    {
        await using var fixture = await Fixture.CreateAsync(new());
        await fixture.ObserveRemoteFailureAsync();

        var exception = await Assert.ThrowsAsync<YouTrackConfigurationException>(
            () => fixture.Host.StartAsync());

        foreach (var setting in ValidSettings().Keys)
            Assert.Contains(setting, exception.Message);
        Assert.False(fixture.Discovery.FirstPoll.Task.IsCompleted);
        Assert.Equal(1, fixture.ConfigurationReads);
        var logs = fixture.Logs.Entries.ToArray();
        var retained = Array.FindIndex(logs, entry => entry.Message.Contains("Stored unresolved operational issue"));
        var validation = Array.FindIndex(logs, entry => entry.Message.Contains("configuration is invalid"));
        Assert.True(retained >= 0 && validation > retained);
        var issue = Assert.Single(await fixture.ReadIssuesAsync(), entry => entry.ScopeType == "Configuration");
        Assert.Equal("YouTrack", issue.ScopeId);
        Assert.Equal("ConfigureDiscovery", issue.Capability);
        Assert.Equal("InvalidConfiguration", issue.ReasonCode);
        Assert.Equal(OperationalAvailability.Unavailable, issue.Availability);
        Assert.Equal(exception.Message, issue.Message);
    }

    [Fact]
    public async Task ConfigurationFailureRecoveryAndRecurrencePreserveRemoteIssue()
    {
        var values = ValidSettings();
        values.Remove(YouTrackConfiguration.SourceIdEnvironmentVariable);
        await using var fixture = await Fixture.CreateAsync(values);
        await fixture.ObserveRemoteFailureAsync();
        var remoteBefore = JsonSerializer.Serialize(Assert.Single(await fixture.ReadIssuesAsync()));

        await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Check.StartAsync(default));
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(1);
        await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Check.StartAsync(default));
        values[YouTrackConfiguration.BaseUrlEnvironmentVariable] = "http://private-url-marker.example";
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(1);
        var failure = await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Check.StartAsync(default));
        var open = Assert.Single(await fixture.ReadIssuesAsync(), issue => issue.ScopeType == "Configuration");
        Assert.Equal(3L, open.ObservationCount);
        Assert.True(open.LastObservedAt > open.FirstObservedAt);
        Assert.Equal(failure.Message, open.Message);
        Assert.All(fixture.Logs.Entries, entry =>
        {
            Assert.DoesNotContain("private-url-marker", entry.Message);
            Assert.DoesNotContain("secret-token-marker", entry.Message);
            Assert.DoesNotContain("private-query-marker", entry.Message);
        });

        fixture.RestartWith(ValidSettings());
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(1);
        await fixture.Host.StartAsync();
        var entriesAtDiscovery = await fixture.Discovery.FirstPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Host.StopAsync();
        var configuration = fixture.Host.Services.GetRequiredService<YouTrackConfiguration>();
        Assert.Same(configuration, fixture.Host.Services.GetRequiredService<YouTrackConfiguration>());
        Assert.Equal(1, fixture.ConfigurationReads);
        var resolved = Assert.Single(await fixture.ReadIssuesAsync(), issue => issue.ScopeType == "Configuration");
        Assert.Equal(open.Id, resolved.Id);
        Assert.Equal(fixture.Clock.Now, resolved.ResolvedAt);
        Assert.Equal(open.LastObservedAt, resolved.LastObservedAt);
        Assert.Equal(open.ObservationCount, resolved.ObservationCount);
        Assert.Contains(entriesAtDiscovery,
            entry => entry.Message.Contains("resolved: Configuration YouTrack"));

        fixture.RestartWith(new());
        fixture.Clock.Now = fixture.Clock.Now.AddMinutes(1);
        await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Check.StartAsync(default));
        var history = await fixture.ReadIssuesAsync();
        var current = Assert.Single(history, issue => issue.ScopeType == "Configuration" && issue.ResolvedAt == null);
        Assert.NotEqual(open.Id, current.Id);
        Assert.Equal(1L, current.ObservationCount);
        Assert.Equal(2, history.Count(issue => issue.ScopeType == "Configuration"));
        Assert.Equal(remoteBefore, JsonSerializer.Serialize(Assert.Single(history, issue => issue.ScopeType == "WorkSource")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenIssueStorePreservesConfigurationDecision(bool valid)
    {
        await using var fixture = await Fixture.CreateAsync(valid ? ValidSettings() : new());
        await using (var scope = fixture.Host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TrailyDbContext>()
                .Database.ExecuteSqlRawAsync("DROP TABLE OperationalIssues");

        if (valid)
        {
            await fixture.Host.StartAsync();
            await fixture.Discovery.FirstPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await fixture.Host.StopAsync();
        }
        else
        {
            await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Host.StartAsync());
            Assert.False(fixture.Discovery.FirstPoll.Task.IsCompleted);
        }

        var fallback = Assert.Single(fixture.Logs.Entries,
            entry => entry.Message.StartsWith("Could not persist the YouTrack configuration observation."));
        Assert.Contains("status is unknown", fallback.Message);
        Assert.Contains("SqliteException", fallback.Message);
        Assert.DoesNotContain("no such table", fallback.Message);
        Assert.DoesNotContain("DROP TABLE", fallback.Message);
    }

    [Fact]
    public async Task ValidStartupPollsWithoutCreatingHealthyIssueRecords()
    {
        await using var fixture = await Fixture.CreateAsync(ValidSettings());

        await fixture.Host.StartAsync();
        var logs = await fixture.Discovery.FirstPoll.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Host.StopAsync();

        Assert.Contains(logs, entry => entry.Message.StartsWith("Startup found 0 stored unresolved"));
        Assert.Equal(1, fixture.ConfigurationReads);
        Assert.Empty(await fixture.ReadIssuesAsync());
    }

    [Fact]
    public async Task CancellationDoesNotReadConfigurationOrChangeIssues()
    {
        await using var fixture = await Fixture.CreateAsync(new());
        await fixture.ObserveRemoteFailureAsync();
        var before = JsonSerializer.Serialize(await fixture.ReadIssuesAsync());
        fixture.Logs.Entries.Clear();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Check.StartAsync(cancellation.Token));

        Assert.Equal(0, fixture.ConfigurationReads);
        Assert.Empty(fixture.Logs.Entries);
        Assert.Equal(before, JsonSerializer.Serialize(await fixture.ReadIssuesAsync()));
    }

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        [YouTrackConfiguration.BaseUrlEnvironmentVariable] = "https://example.invalid/",
        [YouTrackConfiguration.TokenEnvironmentVariable] = " secret-token-marker ",
        [YouTrackConfiguration.DiscoveryQueryEnvironmentVariable] = " private-query-marker ",
        [YouTrackConfiguration.SourceIdEnvironmentVariable] = "test-source"
    };

    private sealed record LogEntry(string Category, string Message);

    private sealed class CapturingLogs : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);
        public void Dispose() { }
        private sealed class Logger(CapturingLogs owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
                => owner.Entries.Enqueue(new(category, formatter(state, exception)));
        }
    }

    private sealed class RecordingDiscovery(CapturingLogs logs) : IWorkItemDiscovery
    {
        public TaskCompletionSource<LogEntry[]> FirstPoll { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(CancellationToken cancellationToken = default)
        {
            FirstPoll.TrySetResult(logs.Entries.ToArray());
            return Task.FromResult<IReadOnlyList<DiscoveredWorkItem>>(Array.Empty<DiscoveredWorkItem>());
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture(SqliteConnection connection) : IAsyncDisposable
    {
        public IHost Host { get; private set; } = null!;
        public RecordingDiscovery Discovery { get; private set; } = null!;
        public CapturingLogs Logs { get; } = new();
        public TestClock Clock { get; } = new();
        public int ConfigurationReads { get; private set; }
        public YouTrackConfigurationStartupCheck Check => Host.Services.GetRequiredService<YouTrackConfigurationStartupCheck>();

        public static async Task<Fixture> CreateAsync(Dictionary<string, string?> settings)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            fixture.RestartWith(settings);
            await using var scope = fixture.Host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<TrailyDbContext>().Database.MigrateAsync();
            fixture.Logs.Entries.Clear();
            return fixture;
        }

        public void RestartWith(Dictionary<string, string?> settings)
        {
            Host?.Dispose();
            ConfigurationReads = 0;
            Logs.Entries.Clear();
            Discovery = new RecordingDiscovery(Logs);
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(Logs);
            builder.Services.AddDbContext<TrailyDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddSingleton<TimeProvider>(Clock);
            builder.Services.AddSingleton<OperationalIssueService>();
            builder.Services.AddSingleton(_ =>
            {
                ConfigurationReads++;
                return YouTrackConfiguration.FromEnvironment(settings.GetValueOrDefault);
            });
            builder.Services.AddHostedService<OperationalIssueStartupReporter>();
            builder.Services.AddSingleton<YouTrackConfigurationStartupCheck>();
            builder.Services.AddHostedService<YouTrackConfigurationStartupCheck>(
                provider => provider.GetRequiredService<YouTrackConfigurationStartupCheck>());
            builder.Services.AddHostedService<WorkItemPollingService>();
            builder.Services.AddSingleton(new WorkItemPollingConfiguration(TimeSpan.FromMinutes(1)));
            builder.Services.AddSingleton<IWorkItemDiscovery>(Discovery);
            builder.Services.AddScoped<WorkItemSynchronizer>();
            Host = builder.Build();
        }

        public Task ObserveRemoteFailureAsync() => Host.Services.GetRequiredService<OperationalIssueService>()
            .ObserveAsync(new(new("WorkSource", "test-source", "DiscoverWorkItems"),
                OperationalAvailability.Unavailable, "AuthenticationFailed", "Credentials were rejected.", 401));

        public async Task<List<OperationalIssue>> ReadIssuesAsync()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TrailyDbContext>()
                .OperationalIssues.AsNoTracking().OrderBy(issue => issue.ScopeType).ThenBy(issue => issue.Id).ToListAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Host.Dispose();
            await connection.DisposeAsync();
        }
    }
}
