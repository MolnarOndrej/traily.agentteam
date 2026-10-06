using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Hosting;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkItems;
using Traily.AgentTeam.WorkSources;

namespace Traily.AgentTeam.Tests;

public sealed class WorkSourceAccessTests
{
    [Theory]
    [InlineData("case-insensitive")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    public async Task ProviderLookupRequiresOneCheckerAndPreservesObservationOrder(string scenario)
    {
        var first = new Checker("YOUTRACK");
        var duplicate = new Checker("youtrack");
        var second = new Checker("OtherProvider");
        IWorkSourceAccessCheck[] checks = scenario switch
        {
            "duplicate" => [first, duplicate, second],
            "missing" => [second],
            _ => [first, second]
        };
        await using var fixture = await Fixture.CreateAsync(checks);
        await fixture.SeedAsync();

        var observations = await fixture.CheckAsync();

        Assert.Equal(new[] { "OperationalDatabase", "first", "a", "b", "second", "c" },
            observations.Select(observation => observation.Scope.Id));
        Assert.Equal(1, second.ConnectionCalls);
        Assert.Equal(new[] { "c" }, second.ProjectCalls);
        Assert.Equal(OperationalAvailability.Available,
            observations.Single(observation => observation.Scope.Id == "c").Availability);
        Assert.Equal(0, duplicate.ConnectionCalls);
        Assert.Empty(duplicate.ProjectCalls);

        if (scenario == "case-insensitive")
        {
            Assert.Equal(1, first.ConnectionCalls);
            Assert.Equal(new[] { "a", "b" }, first.ProjectCalls);
            Assert.All(observations, observation =>
                Assert.Equal(OperationalAvailability.Available, observation.Availability));
            Assert.Empty(await fixture.HistoryAsync());
        }
        else
        {
            Assert.Equal(0, first.ConnectionCalls);
            Assert.Empty(first.ProjectCalls);
            Assert.Equal("AccessCheckUnavailable",
                observations.Single(observation => observation.Scope.Id == "first").ReasonCode);
            Assert.All(observations.Where(observation => observation.Scope.Id is "a" or "b"),
                observation =>
                {
                    Assert.Equal(OperationalAvailability.Unknown, observation.Availability);
                    Assert.Equal("ConnectionNotVerified", observation.ReasonCode);
                });
            var history = await fixture.HistoryAsync();
            Assert.Equal(3, history.Count);
            Assert.All(history, issue => Assert.Null(issue.ResolvedAt));
        }
    }

    [Fact]
    public async Task MultipleProvidersAndProjectsKeepIndependentFailureRecoveryHistory()
    {
        var first = new Checker("YouTrack");
        var second = new Checker("OtherProvider");
        await using var fixture = await Fixture.CreateAsync(first, second);
        await fixture.SeedAsync();
        var discoveryScope = new OperationalScope("WorkSource", "a", "DiscoverWorkItems");
        await fixture.Issues.ObserveAsync(new(discoveryScope, OperationalAvailability.Unavailable,
            "AuthenticationFailed", "An independent discovery failure.", 401));
        first.ProjectAvailability["a"] = OperationalAvailability.Unavailable;

        var observations = await fixture.CheckAsync();
        Assert.Equal(1, first.ConnectionCalls);
        Assert.Equal(1, second.ConnectionCalls);
        Assert.Equal(new[] { "a", "b" }, first.ProjectCalls);
        Assert.Equal(new[] { "c" }, second.ProjectCalls);
        Assert.Equal(OperationalAvailability.Available, observations.Single(value => value.Scope.Id == "b").Availability);
        Assert.Equal(OperationalAvailability.Available, observations.Single(value => value.Scope.Id == "c").Availability);
        var failed = Assert.Single(await fixture.HistoryAsync(), value => value.Capability == "ReadProject");
        Assert.Equal("a", failed.ScopeId);

        await fixture.CheckAsync();
        var repeated = Assert.Single(await fixture.HistoryAsync(), value => value.Capability == "ReadProject");
        Assert.Equal(failed.Id, repeated.Id);
        Assert.Equal(2, repeated.ObservationCount);
        first.ProjectAvailability["a"] = OperationalAvailability.Available;
        await fixture.CheckAsync();
        var recovered = Assert.Single(await fixture.HistoryAsync(), value => value.Capability == "ReadProject");
        Assert.Equal(failed.Id, recovered.Id);
        Assert.NotNull(recovered.ResolvedAt);
        Assert.Equal(2, recovered.ObservationCount);

        first.ProjectAvailability["a"] = OperationalAvailability.Unknown;
        await fixture.CheckAsync();
        var history = await fixture.HistoryAsync();
        var recurrence = Assert.Single(history, value => value.Capability == "ReadProject" && value.ResolvedAt == null);
        Assert.NotEqual(failed.Id, recurrence.Id);
        Assert.Equal(1, recurrence.ObservationCount);
        Assert.Null(Assert.Single(history, value => value.Capability == "DiscoverWorkItems").ResolvedAt);
    }

    [Fact]
    public async Task FailedAuthenticationSkipsOnlyDependentProjectsAndDoesNotResolveTheirIssues()
    {
        var first = new Checker("YouTrack");
        var second = new Checker("OtherProvider");
        await using var fixture = await Fixture.CreateAsync(first, second);
        await fixture.SeedAsync();
        await fixture.Issues.ObserveAsync(new(new("WorkSource", "a", "ReadProject"),
            OperationalAvailability.Unavailable, "AccessDenied", "Project access denied.", 403));
        first.ConnectionAvailability = OperationalAvailability.Unavailable;

        var observations = await fixture.CheckAsync();

        Assert.Empty(first.ProjectCalls);
        Assert.Equal(new[] { "c" }, second.ProjectCalls);
        Assert.Equal("ConnectionNotVerified", observations.Single(value => value.Scope.Id == "a").ReasonCode);
        Assert.Equal(OperationalAvailability.Unknown, observations.Single(value => value.Scope.Id == "a").Availability);
        Assert.Null((await fixture.HistoryAsync()).Single(value => value.ScopeId == "a").ResolvedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrDuplicateProviderCheckersAreUnknownWithoutInvokingThem(bool duplicate)
    {
        var first = new Checker("YouTrack");
        var second = new Checker("OtherProvider");
        await using var fixture = duplicate
            ? await Fixture.CreateAsync(first, new Checker("youtrack"), second)
            : await Fixture.CreateAsync(second);
        await fixture.SeedAsync();
        var observations = await fixture.CheckAsync();
        Assert.Equal("AccessCheckUnavailable", observations.Single(value => value.Scope.Id == "first").ReasonCode);
        Assert.Equal(0, first.ConnectionCalls);
        Assert.Equal(1, second.ConnectionCalls);
        Assert.Equal(OperationalAvailability.Available, observations.Single(value => value.Scope.Id == "c").Availability);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdapterExceptionsAndIncorrectScopesCannotAffectAnotherProvider(bool incorrectScope)
    {
        var first = new Checker("YouTrack") { ThrowOnProject = !incorrectScope, WrongProjectScope = incorrectScope };
        var second = new Checker("OtherProvider");
        await using var fixture = await Fixture.CreateAsync(first, second);
        await fixture.SeedAsync();
        var observations = await fixture.CheckAsync();
        Assert.Equal("AccessCheckFailed", observations.Single(value => value.Scope.Id == "a").ReasonCode);
        Assert.Equal(OperationalAvailability.Available, observations.Single(value => value.Scope.Id == "c").Availability);
        Assert.All(await fixture.HistoryAsync(), value => Assert.DoesNotContain("private-marker", value.Message));
    }

    [Fact]
    public async Task IssuePersistenceFailureDoesNotReplaceCapabilityResults()
    {
        await using var fixture = await Fixture.CreateAsync(new Checker("YouTrack"), new Checker("OtherProvider"));
        await fixture.SeedAsync();
        await fixture.DatabaseAsync(database => database.Database.ExecuteSqlRawAsync("DROP TABLE OperationalIssues"));
        var observations = await fixture.CheckAsync();
        Assert.Equal(6, observations.Count);
        Assert.All(observations, value => Assert.Equal(OperationalAvailability.Available, value.Availability));
    }

    [Fact]
    public async Task ConfigurationReadFailureDoesNotResolvePreviouslyObservedAccessFailure()
    {
        await using var fixture = await Fixture.CreateAsync(new Checker("YouTrack"));
        await fixture.Issues.ObserveAsync(new(new("WorkSource", "saved-source", "ReadProject"),
            OperationalAvailability.Unavailable, "AccessDenied", "Saved access failure."));
        await fixture.DatabaseAsync(database => database.Database.ExecuteSqlRawAsync("DROP TABLE WorkSourceConnections"));
        var observation = Assert.Single(await fixture.CheckAsync());
        Assert.Equal("ConfigurationReadFailed", observation.ReasonCode);
        Assert.Equal(OperationalAvailability.Unknown, observation.Availability);
        Assert.All(await fixture.HistoryAsync(), value => Assert.Null(value.ResolvedAt));
    }

    [Fact]
    public async Task CallerCancellationStopsTheSweepWithoutChangingHistory()
    {
        var first = new Checker("YouTrack");
        await using var fixture = await Fixture.CreateAsync(first);
        await fixture.SeedAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.CheckAsync(cancellation.Token));
        Assert.Empty(await fixture.HistoryAsync());
        Assert.Equal(0, first.ConnectionCalls);
    }

    [Fact]
    public async Task StartupAndDiscoveryWrapperCheckAccessBeforeDiscoveryWithoutBlockingIt()
    {
        var first = new Checker("YouTrack") { ConnectionAvailability = OperationalAvailability.Unavailable };
        var second = new Checker("OtherProvider");
        await using var fixture = await Fixture.CreateAsync(first, second);
        await fixture.SeedAsync();
        await new WorkSourceAccessStartupCheck(fixture.Provider.GetRequiredService<IServiceScopeFactory>())
            .StartAsync(default);
        Assert.Equal(1, first.ConnectionCalls);
        await using var scope = fixture.Provider.CreateAsyncScope();
        var inner = new Discovery(() =>
        {
            Assert.Equal(2, first.ConnectionCalls);
            Assert.Equal(2, second.ConnectionCalls);
        });
        var discovery = new AccessCheckedWorkItemDiscovery(inner,
            scope.ServiceProvider.GetRequiredService<WorkSourceAccessService>());
        Assert.Empty(await discovery.FindReadyAsync());
        Assert.Equal(1, inner.Calls);
        Assert.Equal(2, (await fixture.HistoryAsync()).Single(value => value.ScopeId == "first").ObservationCount);
    }

    [Theory]
    [InlineData(null, "0-1")]
    [InlineData("missing", "0-1")]
    [InlineData("first", null)]
    [InlineData("first", "   ")]
    [InlineData("", "0-1")]
    public async Task FinalSchemaRejectsProjectsWithoutValidManagementToolMappings(string? connectionId, string? externalId)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.DatabaseAsync(async database =>
        {
            var exception = await Assert.ThrowsAsync<SqliteException>(() => database.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO ManagedProjects (SourceId, Name, CreatedAt, UpdatedAt, WorkSourceConnectionId, ExternalProjectId) VALUES ('invalid', 'Invalid', '2026-10-05', '2026-10-05', {connectionId}, {externalId})"));
            Assert.Equal(19, exception.SqliteErrorCode);
            Assert.False(await database.ManagedProjects.AnyAsync(value => value.SourceId == "invalid"));
        });
    }

    [Fact]
    public async Task ReferencedConnectionCannotBeDeleted()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        await fixture.DatabaseAsync(async database =>
        {
            var exception = await Assert.ThrowsAsync<SqliteException>(() =>
                database.Database.ExecuteSqlRawAsync("DELETE FROM WorkSourceConnections WHERE Id = 'first'"));
            Assert.Equal(19, exception.SqliteErrorCode);
            Assert.Equal(3, await database.ManagedProjects.CountAsync());
            Assert.Equal(2, await database.WorkSourceConnections.CountAsync());
        });
    }

    [Fact]
    public async Task SingleMigrationRejectsExistingProjectsBeforeChangingSchemaOrHistory()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var database = new TrailyDbContext(new DbContextOptionsBuilder<TrailyDbContext>()
            .UseSqlite(connection).Options);
        var migrator = database.GetService<IMigrator>();
        await migrator.MigrateAsync("20261001111418_AddOperationalIssues");
        await database.Database.ExecuteSqlRawAsync(
            "INSERT INTO ManagedProjects (SourceId, Name, CreatedAt, UpdatedAt) VALUES ('existing', 'Retained project', '2026-10-05', '2026-10-05')");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => migrator.MigrateAsync());

        Assert.Contains("ProvideExplicitProjectBackfillBeforeApplyingWorkSourceMigration", exception.Message);
        Assert.Equal("20261001111418_AddOperationalIssues", (await database.Database.GetAppliedMigrationsAsync()).Last());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Name FROM ManagedProjects WHERE SourceId = 'existing'";
        Assert.Equal("Retained project", await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name = 'WorkSourceConnections'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
        Assert.Single(database.Database.GetMigrations(), value => value.Contains("WorkSource"));
    }

    private sealed class Checker(string providerId) : IWorkSourceAccessCheck
    {
        public string ProviderId => providerId;
        public int ConnectionCalls { get; private set; }
        public List<string> ProjectCalls { get; } = [];
        public OperationalAvailability ConnectionAvailability { get; set; } = OperationalAvailability.Available;
        public Dictionary<string, OperationalAvailability> ProjectAvailability { get; } = [];
        public bool ThrowOnProject { get; init; }
        public bool WrongProjectScope { get; init; }
        public Task<OperationalObservation> CheckConnectionAsync(WorkSourceConnection connection, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            ConnectionCalls++;
            return Task.FromResult(Observe(new("WorkSourceConnection", connection.Id, "Authenticate"), ConnectionAvailability));
        }
        public Task<OperationalObservation> CheckProjectAsync(WorkSourceConnection connection, ManagedProject project, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            ProjectCalls.Add(project.SourceId);
            if (ThrowOnProject) throw new InvalidOperationException("private-marker");
            var availability = ProjectAvailability.GetValueOrDefault(project.SourceId, OperationalAvailability.Available);
            return Task.FromResult(Observe(new("WorkSource", WrongProjectScope ? "wrong" : project.SourceId, "ReadProject"), availability));
        }
        private static OperationalObservation Observe(OperationalScope scope, OperationalAvailability availability) =>
            availability == OperationalAvailability.Available
                ? new(scope, availability)
                : new(scope, availability, "AccessDenied", "A safe test access failure.", 403);
    }

    private sealed class Discovery(Action beforeDiscovery) : IWorkItemDiscovery
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            beforeDiscovery();
            Calls++;
            return Task.FromResult<IReadOnlyList<DiscoveredWorkItem>>([]);
        }
    }

    private sealed class Fixture(SqliteConnection connection, ServiceProvider provider) : IAsyncDisposable
    {
        public ServiceProvider Provider => provider;
        public OperationalIssueService Issues => provider.GetRequiredService<OperationalIssueService>();
        public static async Task<Fixture> CreateAsync(params IWorkSourceAccessCheck[] checks)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<TrailyDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<OperationalIssueService>();
            foreach (var check in checks) services.AddSingleton(check);
            services.AddScoped<WorkSourceAccessService>();
            var provider = services.BuildServiceProvider();
            var fixture = new Fixture(connection, provider);
            await fixture.DatabaseAsync(database => database.Database.MigrateAsync());
            return fixture;
        }
        public async Task SeedAsync() => await DatabaseAsync(async database =>
        {
            var now = DateTimeOffset.UtcNow;
            database.WorkSourceConnections.AddRange(
                new WorkSourceConnection { Id = "first", ProviderId = "YouTrack", BaseUrl = "https://example.invalid", CreatedAt = now, UpdatedAt = now },
                new WorkSourceConnection { Id = "second", ProviderId = "OtherProvider", BaseUrl = "https://other.invalid", CreatedAt = now, UpdatedAt = now });
            foreach (var source in new[] { "a", "b", "c" })
                database.ManagedProjects.Add(new ManagedProject
                {
                    SourceId = source, Name = source, WorkSourceConnectionId = source == "c" ? "second" : "first",
                    ExternalProjectId = "0-1", CreatedAt = now, UpdatedAt = now
                });
            await database.SaveChangesAsync();
        });
        public async Task<IReadOnlyList<OperationalObservation>> CheckAsync(CancellationToken token = default)
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<WorkSourceAccessService>().CheckAllAsync(token);
        }
        public async Task<List<OperationalIssue>> HistoryAsync()
        {
            await using var scope = provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<TrailyDbContext>().OperationalIssues.AsNoTracking().ToListAsync();
        }
        public async Task DatabaseAsync(Func<TrailyDbContext, Task> action)
        {
            await using var scope = provider.CreateAsyncScope();
            await action(scope.ServiceProvider.GetRequiredService<TrailyDbContext>());
        }
        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
