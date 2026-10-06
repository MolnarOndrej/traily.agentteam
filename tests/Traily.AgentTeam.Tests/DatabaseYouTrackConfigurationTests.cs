using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Hosting;
using Traily.AgentTeam.Integrations.YouTrack;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Tests;

public sealed class DatabaseYouTrackConfigurationTests
{
    [Fact]
    public async Task TokenIsEncryptedAtRestAndUpdatesWithoutChangingConnectionIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddConnectionAsync("first", "https://first.invalid/youtrack");
        await fixture.Store.SetAccessTokenAsync("first", " first-private-token ");
        var encrypted = await fixture.ReadAsync(db => db.YouTrackConnectionConfigurations.SingleAsync());
        Assert.DoesNotContain("first-private-token", encrypted.ProtectedAccessToken);
        var settings = await fixture.Store.GetConnectionAsync("first");
        Assert.Equal("first-private-token", settings.AccessToken);
        Assert.Equal(new Uri("https://first.invalid/youtrack/"), settings.BaseAddress);
        Assert.DoesNotContain(settings.AccessToken, settings.ToString());
        Assert.DoesNotContain("first.invalid", settings.ToString());

        await fixture.Store.SetAccessTokenAsync("first", "replacement-private-token");
        var updated = await fixture.ReadAsync(db => db.YouTrackConnectionConfigurations.SingleAsync());
        Assert.Equal("first", updated.ConnectionId);
        Assert.NotEqual(encrypted.ProtectedAccessToken, updated.ProtectedAccessToken);
        Assert.Equal("replacement-private-token", (await fixture.Store.GetConnectionAsync("first")).AccessToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CopiedOrCorruptCiphertextProducesSafeConfigurationFailure(bool corrupt)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddConnectionAsync("first");
        await fixture.AddConnectionAsync("second");
        await fixture.Store.SetAccessTokenAsync("first", "private-marker-token");
        var original = await fixture.ReadAsync(db => db.YouTrackConnectionConfigurations.SingleAsync());
        await fixture.EditAsync(db =>
        {
            db.YouTrackConnectionConfigurations.Add(new()
            {
                ConnectionId = "second",
                ProtectedAccessToken = corrupt ? "corrupt-private-marker" : original.ProtectedAccessToken
            });
            return Task.CompletedTask;
        });
        var error = await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Store.GetConnectionAsync("second"));
        Assert.Contains("could not be decrypted", error.Message);
        Assert.DoesNotContain("private-marker", error.ToString());
        Assert.DoesNotContain(original.ProtectedAccessToken, error.ToString());
    }

    [Fact]
    public async Task MissingCredentialDoesNotInitializeKeyProvider()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddConnectionAsync("first");
        await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Store.GetConnectionAsync("first"));
        Assert.False(fixture.Provider.GetRequiredService<Lazy<AccessTokenProtector>>().IsValueCreated);
    }

    [Theory]
    [InlineData("http://example.invalid")]
    [InlineData("https://private-marker@example.invalid")]
    [InlineData("https://example.invalid/?private-marker")]
    [InlineData("https://example.invalid/#private-marker")]
    public async Task UnsafeUrlIsRejectedBeforeDecryption(string url)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddConnectionAsync("first", url);
        await fixture.EditAsync(db =>
        {
            db.YouTrackConnectionConfigurations.Add(new() { ConnectionId = "first", ProtectedAccessToken = "not-needed" });
            return Task.CompletedTask;
        });
        var error = await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Store.GetConnectionAsync("first"));
        Assert.DoesNotContain("private-marker", error.ToString());
        Assert.False(fixture.Provider.GetRequiredService<Lazy<AccessTokenProtector>>().IsValueCreated);
    }

    [Fact]
    public async Task TokenCannotBeProvisionedForMissingOrDifferentProviderConnection()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddConnectionAsync("other", providerId: "OtherProvider");
        foreach (var connection in new[] { "missing", "other" })
            await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Store.SetAccessTokenAsync(connection, "private-token"));
        Assert.Equal(0, await fixture.ReadAsync(db => db.YouTrackConnectionConfigurations.CountAsync()));
        Assert.False(fixture.Provider.GetRequiredService<Lazy<AccessTokenProtector>>().IsValueCreated);
    }

    [Fact]
    public void ProtectedTokenSurvivesKeyProviderRecreationAndRejectsAnotherPurposeOrKeyRing()
    {
        // A private-key certificate keeps this test independent of the Windows account's DPAPI store.
        var directory = Path.Combine(Path.GetTempPath(), "traily-protection-" + Guid.NewGuid().ToString("N"));
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Traily test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        try
        {
            AccessTokenProtector Create() => new(DataProtectionProvider.Create(new DirectoryInfo(directory), builder =>
            {
                builder.SetApplicationName("Traily.AgentTeam");
                builder.ProtectKeysWithCertificate(certificate);
            }));
            var ciphertext = Create().Protect("connection-a", "private-marker-token");
            Assert.Equal("private-marker-token", Create().Unprotect("connection-a", ciphertext));
            Assert.Throws<CryptographicException>(() => Create().Unprotect("connection-b", ciphertext));
            var other = new AccessTokenProtector(new EphemeralDataProtectionProvider());
            Assert.Throws<CryptographicException>(() => other.Unprotect("connection-a", ciphertext));
            var keyFiles = Directory.GetFiles(directory, "*.xml");
            Assert.NotEmpty(keyFiles);
            foreach (var file in keyFiles)
            {
                var xml = File.ReadAllText(file);
                Assert.Contains("encryptedSecret", xml);
                Assert.DoesNotContain("private-marker-token", xml);
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("project: {{project}} assignee: {{unknown}}")]
    [InlineData("assignee: {{assignee}}")]
    [InlineData("project: {{project}} assignee: {{assignee}} {{unknown}}")]
    [InlineData("project: {{project}} assignee: {{assignee}} {{broken")]
    public async Task InvalidTemplateIsReportedAtStartupWithoutNetworkRequests(string template)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source", "connection");
        await fixture.EditAsync(async db =>
            (await db.YouTrackProjectConfigurations.SingleAsync()).DiscoveryQueryTemplate = template);
        await fixture.Provider.GetRequiredService<WorkSourceConfigurationStartupCheck>().StartAsync(CancellationToken.None);
        var issue = Assert.Single(await fixture.HistoryAsync());
        Assert.Equal("Configuration", issue.ScopeType);
        Assert.Equal("source", issue.ScopeId);
        Assert.Equal("InvalidConfiguration", issue.ReasonCode);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Fact]
    public void ProductionCertificateRegistrationIsLazyAndReusesPersistedProtectedKeys()
    {
        var directory = Path.Combine(Path.GetTempPath(), "traily-certificate-" + Guid.NewGuid().ToString("N"));
        var variables = new[] { "TRAILY_DATA_PROTECTION_KEYS_PATH", "TRAILY_DATA_PROTECTION_CERTIFICATE_PATH",
            "TRAILY_DATA_PROTECTION_CERTIFICATE_PASSWORD" };
        var previous = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Traily registration test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        try
        {
            Directory.CreateDirectory(directory);
            var certificatePath = Path.Combine(directory, "test.pfx");
            var keyPath = Path.Combine(directory, "keys");
            File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, "test-only-password"));
            Environment.SetEnvironmentVariable(variables[0], keyPath);
            Environment.SetEnvironmentVariable(variables[1], certificatePath);
            Environment.SetEnvironmentVariable(variables[2], "test-only-password");
            ServiceProvider CreateProvider()
            {
                var services = new ServiceCollection();
                CredentialProtectionConfiguration.Register(services);
                return services.BuildServiceProvider();
            }
            string ciphertext;
            using (var first = CreateProvider())
            {
                Assert.False(Directory.Exists(keyPath));
                ciphertext = first.GetRequiredService<AccessTokenProtector>().Protect("connection", "test-token");
                Assert.True(Directory.Exists(keyPath));
            }
            using var second = CreateProvider();
            Assert.Equal("test-token", second.GetRequiredService<AccessTokenProtector>().Unprotect("connection", ciphertext));
            Assert.All(Directory.GetFiles(keyPath, "*.xml"), file => Assert.Contains("encryptedSecret", File.ReadAllText(file)));
        }
        finally
        {
            foreach (var variable in variables) Environment.SetEnvironmentVariable(variable, previous[variable]);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StartupConfigurationFailureRecoveryPreservesUnrelatedDiscoveryIssue()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddConnectionAsync("connection");
        await fixture.AddProjectAsync("source", "connection");
        var startup = fixture.Provider.GetRequiredService<WorkSourceConfigurationStartupCheck>();
        await fixture.Provider.GetRequiredService<OperationalIssueService>().ObserveAsync(new(
            new("WorkSource", "source", "DiscoverWorkItems"), OperationalAvailability.Unknown,
            "ConnectionFailed", "A retained discovery failure."));
        await startup.StartAsync(CancellationToken.None);
        await startup.StartAsync(CancellationToken.None);
        var failure = Assert.Single(await fixture.HistoryAsync(), issue => issue.ScopeType == "Configuration");
        Assert.Null(failure.ResolvedAt);
        Assert.Equal(2, failure.ObservationCount);
        await fixture.Store.SetAccessTokenAsync("connection", "connection-token");
        await startup.StartAsync(CancellationToken.None);
        var history = await fixture.HistoryAsync();
        Assert.NotNull(Assert.Single(history, issue => issue.ScopeType == "Configuration").ResolvedAt);
        Assert.Null(Assert.Single(history, issue => issue.ScopeType == "WorkSource").ResolvedAt);
        Assert.Empty(fixture.Handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProviderConfigurationRequiresAnExistingConnectionOrProject(bool connectionConfiguration)
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.EditAsync(db =>
        {
            if (connectionConfiguration)
                db.YouTrackConnectionConfigurations.Add(new() { ConnectionId = "missing", ProtectedAccessToken = "test-value" });
            else
                db.YouTrackProjectConfigurations.Add(new() { SourceId = "missing" });
            return Task.CompletedTask;
        }));
    }

    [Theory]
    [InlineData("bad} or Assignee: me")]
    [InlineData("bad\\name")]
    [InlineData("bad\nname")]
    public void UnsafeSearchIdentityCannotBroadenQuery(string login)
    {
        Assert.Throws<YouTrackConfigurationException>(() => YouTrackConfigurationStore.CreateDiscoveryQuery(
            new(), "FIN", login));
    }

    [Fact]
    public async Task MultipleAgentsUseCurrentRemoteLoginsAndOneSharedConnectionToken()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source", "connection");
        await fixture.AddAgentAsync("source", "team-lead", "2-1", storedLogin: "old-team-lead-login");
        await fixture.AddAgentAsync("source", "net-expert", "2-2");
        await fixture.AddAgentAsync("source", "disabled", "2-3", enabled: false);
        await fixture.AddAgentAsync("source", "deleted", "2-4", deletionRequested: true);

        var results = await fixture.SyncAsync();
        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.Equal(WorkItemSyncOutcome.New, result.Outcome));
        var jobs = await fixture.ReadAsync(db => db.WorkItemJobs.AsNoTracking().ToListAsync());
        Assert.Equal(new[] { "net-expert", "team-lead" }, jobs.Select(job => job.AgentId).Order().ToArray());
        Assert.All(jobs, job => Assert.Equal(WorkItemJobStatus.Queued, job.Status));
        Assert.DoesNotContain(fixture.Handler.Requests, request => request.Path.Contains("2-3") || request.Path.Contains("2-4"));
        var queries = fixture.Handler.Requests.Where(request => request.Path.EndsWith("/api/issues")).ToArray();
        Assert.Equal(2, queries.Length);
        Assert.Contains(queries, request => request.Query.Contains("{current-2-1}"));
        Assert.Contains(queries, request => request.Query.Contains("{current-2-2}"));
        Assert.DoesNotContain(queries, request => request.Query.Contains("old-team-lead-login"));
        Assert.All(fixture.Handler.Requests, request => Assert.Equal("connection-token", request.Token));
        Assert.Empty(await fixture.HistoryAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateProjectNamesAndRemoteIdsRemainIsolatedAcrossConnections(bool sameServer)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source-a", "connection-a", "https://first.invalid");
        await fixture.SeedSourceAsync("source-b", "connection-b", sameServer ? "https://first.invalid" : "https://second.invalid");
        await fixture.AddAgentAsync("source-a", "agent-a", "2-1");
        await fixture.AddAgentAsync("source-b", "agent-b", "2-1");
        fixture.Handler.TitleByToken["connection-a-token"] = "Customer A task";
        fixture.Handler.TitleByToken["connection-b-token"] = "Customer B task";
        var results = await fixture.SyncAsync();
        Assert.Equal(2, results.Count);
        var jobs = await fixture.ReadAsync(db => db.WorkItemJobs.AsNoTracking().ToListAsync());
        Assert.Equal(2, jobs.Count);
        Assert.Equal(new[] { "source-a", "source-b" }, jobs.Select(job => job.SourceId).Order().ToArray());
        Assert.Single(jobs.Select(job => job.ExternalWorkItemId).Distinct());
        Assert.Equal("Customer A task", Assert.Single(jobs, job => job.SourceId == "source-a").Title);
        Assert.Equal("Customer B task", Assert.Single(jobs, job => job.SourceId == "source-b").Title);
        foreach (var sourceId in new[] { "source-a", "source-b" })
        {
            var ticket = await fixture.Source.GetRequiredAsync(sourceId, "3-1");
            Assert.Equal("3-1", ticket.ExternalWorkItemId);
            Assert.Equal(sourceId == "source-a" ? "Customer A task" : "Customer B task", ticket.Title);
        }
        Assert.Contains(fixture.Handler.Requests, request => request.Token == "connection-a-token" && request.Host == "first.invalid");
        Assert.Contains(fixture.Handler.Requests, request => request.Token == "connection-b-token" &&
            request.Host == (sameServer ? "first.invalid" : "second.invalid"));
        Assert.All(await fixture.SyncAsync(), result => Assert.Equal(WorkItemSyncOutcome.AlreadyQueued, result.Outcome));
        Assert.Equal(2, await fixture.ReadAsync(db => db.WorkItemJobs.CountAsync()));
    }

    [Fact]
    public async Task TwoSelectedProjectsShareOneAccountConnectionWithoutSharingQueueIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("first-project", "connection");
        await fixture.AddProjectAsync("second-project", "connection", "0-2");
        await fixture.AddAgentAsync("first-project", "agent-a", "2-1");
        await fixture.AddAgentAsync("second-project", "agent-b", "2-1");
        fixture.Handler.ProjectForIssues = request => request.Query.Contains("{FIN2}") ? "0-2" : "0-1";
        Assert.Equal(2, (await fixture.SyncAsync()).Count);
        Assert.Equal(1, await fixture.ReadAsync(db => db.YouTrackConnectionConfigurations.CountAsync()));
        Assert.All(fixture.Handler.Requests, request => Assert.Equal("connection-token", request.Token));
    }

    [Fact]
    public async Task FailureRecoveryAndRecurrenceAreScopedToOneSource()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source-a", "connection-a", "https://first.invalid");
        await fixture.SeedSourceAsync("source-b", "connection-b", "https://second.invalid");
        await fixture.AddAgentAsync("source-a", "agent-a", "2-1");
        await fixture.AddAgentAsync("source-b", "agent-b", "2-1");
        fixture.Handler.FailedHost = "first.invalid";
        var items = await fixture.Source.FindReadyAsync();
        Assert.Equal("source-b", Assert.Single(items).SourceId);
        var failure = Assert.Single(await fixture.HistoryAsync());
        Assert.Equal("source-a", failure.ScopeId);
        Assert.Equal("DiscoverWorkItems", failure.Capability);
        Assert.DoesNotContain("private-response-marker", failure.Message);

        fixture.Handler.FailedHost = null;
        Assert.Equal(2, (await fixture.Source.FindReadyAsync()).Count);
        Assert.NotNull(Assert.Single(await fixture.HistoryAsync()).ResolvedAt);
        fixture.Handler.FailedHost = "first.invalid";
        await fixture.Source.FindReadyAsync();
        var history = await fixture.HistoryAsync();
        Assert.Equal(2, history.Count);
        Assert.Single(history, issue => issue.ResolvedAt is null);
        Assert.All(history, issue => Assert.Equal("source-a", issue.ScopeId));
    }

    [Fact]
    public async Task SourceWithNoActiveAccountsDoesNotResolveRetainedDiscoveryFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source", "connection");
        await fixture.Provider.GetRequiredService<OperationalIssueService>().ObserveAsync(new(
            new("WorkSource", "source", "DiscoverWorkItems"), OperationalAvailability.Unknown,
            "ConnectionFailed", "Previous request failed."));
        Assert.Empty(await fixture.Source.FindReadyAsync());
        Assert.Empty(fixture.Handler.Requests);
        Assert.Null(Assert.Single(await fixture.HistoryAsync()).ResolvedAt);
    }

    [Fact]
    public async Task MissingProjectSettingsDoesNotPreventHealthySourceDiscoveryAndRecovers()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source-a", "connection-a");
        await fixture.SeedSourceAsync("source-b", "connection-b");
        await fixture.AddAgentAsync("source-a", "agent-a", "2-1");
        await fixture.AddAgentAsync("source-b", "agent-b", "2-1");
        await fixture.EditAsync(async db => db.YouTrackProjectConfigurations.Remove(
            await db.YouTrackProjectConfigurations.SingleAsync(configuration => configuration.SourceId == "source-a")));
        Assert.Equal("source-b", Assert.Single(await fixture.Source.FindReadyAsync()).SourceId);
        var issue = Assert.Single(await fixture.HistoryAsync());
        Assert.Equal("Configuration", issue.ScopeType);
        Assert.Equal("source-a", issue.ScopeId);
        await fixture.EditAsync(db =>
        {
            db.YouTrackProjectConfigurations.Add(new() { SourceId = "source-a" });
            return Task.CompletedTask;
        });
        Assert.Equal(2, (await fixture.Source.FindReadyAsync()).Count);
        Assert.NotNull(Assert.Single(await fixture.HistoryAsync()).ResolvedAt);
    }

    [Fact]
    public async Task AccessChecksAndTicketReadsUseTheSameConnectionToken()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source", "connection", "https://first.invalid/youtrack");
        var connection = await fixture.ReadAsync(db => db.WorkSourceConnections.SingleAsync());
        var project = await fixture.ReadAsync(db => db.ManagedProjects.SingleAsync());
        var checker = new YouTrackWorkSourceAccessCheck(fixture.Client, fixture.Store);
        Assert.Equal(OperationalAvailability.Available, (await checker.CheckConnectionAsync(connection)).Availability);
        Assert.Equal(OperationalAvailability.Available, (await checker.CheckProjectAsync(connection, project)).Availability);
        await fixture.Source.GetRequiredAsync("source", "3-1");
        Assert.Equal(3, fixture.Handler.Requests.Count);
        Assert.All(fixture.Handler.Requests, request =>
        {
            Assert.Equal("connection-token", request.Token);
            Assert.StartsWith("/youtrack/api/", request.Path);
        });
    }

    [Theory]
    [InlineData("project")]
    [InlineData("user")]
    [InlineData("issue-project")]
    public async Task MismatchedRemoteIdentityDoesNotQueueWork(string mismatch)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source", "connection");
        await fixture.AddAgentAsync("source", "agent", "2-1");
        fixture.Handler.Mismatch = mismatch;
        Assert.Empty(await fixture.SyncAsync());
        Assert.Equal(0, await fixture.ReadAsync(db => db.WorkItemJobs.CountAsync()));
        Assert.Equal("InvalidDiscoveryResponse", Assert.Single(await fixture.HistoryAsync()).ReasonCode);
    }

    [Fact]
    public async Task UnexpectedAssigneeIsExcludedEvenWhenServerReturnsIt()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source", "connection");
        await fixture.AddAgentAsync("source", "agent", "2-1");
        fixture.Handler.AssigneeOverride = "2-99";
        Assert.Empty(await fixture.SyncAsync());
        Assert.Equal(0, await fixture.ReadAsync(db => db.WorkItemJobs.CountAsync()));
    }

    [Fact]
    public async Task FullReadRejectsWrongProjectAndMissingSourceWithoutCrossConnectionFallback()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source", "connection");
        await Assert.ThrowsAsync<YouTrackConfigurationException>(() => fixture.Source.GetRequiredAsync("missing", "3-1"));
        Assert.Empty(fixture.Handler.Requests);
        fixture.Handler.Mismatch = "issue-project";
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.GetRequiredAsync("source", "3-1"));
        Assert.Single(fixture.Handler.Requests);
    }

    [Fact]
    public async Task CallerCancellationPropagatesWithoutCreatingFailureHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedSourceAsync("source", "connection");
        await fixture.AddAgentAsync("source", "agent", "2-1");
        using var cancellation = new CancellationTokenSource();
        fixture.Handler.BeforeResponse = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Source.FindReadyAsync(cancellation.Token));
        Assert.Empty(await fixture.HistoryAsync());
    }

    private sealed record Request(string Host, string Path, string Query, string? Token);

    private sealed class Handler : HttpMessageHandler
    {
        public List<Request> Requests { get; } = [];
        public Dictionary<string, string> TitleByToken { get; } = [];
        public string? FailedHost { get; set; }
        public string? Mismatch { get; set; }
        public string? AssigneeOverride { get; set; }
        public Action? BeforeResponse { get; set; }
        public Func<Request, string>? ProjectForIssues { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
        {
            var uri = message.RequestUri!;
            var request = new Request(uri.Host, uri.AbsolutePath, Uri.UnescapeDataString(uri.Query), message.Headers.Authorization?.Parameter);
            Requests.Add(request);
            BeforeResponse?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Host == FailedHost)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("private-response-marker") });
            object body;
            if (request.Path.Contains("/admin/projects/"))
            {
                var id = request.Path.Split('/').Last();
                body = new { id = Mismatch == "project" ? "0-99" : id, shortName = id == "0-2" ? "FIN2" : "FIN" };
            }
            else if (request.Path.Contains("/users/"))
            {
                var id = request.Path.Split('/').Last();
                body = new { id = Mismatch == "user" ? "2-99" : id, login = "current-" + id };
            }
            else
            {
                var assignee = AssigneeOverride ?? (request.Query.Contains("current-2-2") ? "2-2" : "2-1");
                var project = Mismatch == "issue-project" ? "0-99" : ProjectForIssues?.Invoke(request) ?? "0-1";
                var issue = new
                {
                    id = assignee == "2-2" ? "3-2" : "3-1", idReadable = assignee == "2-2" ? "FIN-2" : "FIN-1",
                    summary = TitleByToken.GetValueOrDefault(request.Token ?? string.Empty, "Finly task"),
                    description = "Current description", updated = 1790000000000L, project = new { id = project },
                    customFields = new object[]
                    {
                        new { name = "Stage", value = new { id = "stage-1", name = "To Do" } },
                        new { name = "Assignee", value = new { id = assignee, login = "current-" + assignee, fullName = "Agent" } }
                    }
                };
                body = request.Path.EndsWith("/api/issues") ? new[] { issue } : issue;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class Fixture(SqliteConnection connection, ServiceProvider provider, Handler handler, HttpClient client) : IAsyncDisposable
    {
        public ServiceProvider Provider => provider;
        public Handler Handler => handler;
        public HttpClient Client => client;
        public YouTrackConfigurationStore Store => provider.GetRequiredService<YouTrackConfigurationStore>();
        public DatabaseYouTrackWorkItemSource Source => provider.GetRequiredService<DatabaseYouTrackWorkItemSource>();

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var handler = new Handler();
            var client = new HttpClient(handler);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<TrailyDbContext>(options => options.UseSqlite(connection));
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.AddSingleton<AccessTokenProtector>();
            services.AddSingleton(p => new Lazy<AccessTokenProtector>(() => p.GetRequiredService<AccessTokenProtector>()));
            services.AddSingleton<YouTrackConfigurationStore>();
            services.AddSingleton<OperationalIssueService>();
            services.AddSingleton(client);
            services.AddSingleton<DatabaseYouTrackWorkItemSource>();
            services.AddSingleton<WorkSourceConfigurationStartupCheck>();
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            var fixture = new Fixture(connection, provider, handler, client);
            await fixture.ReadAsync(async db => { await db.Database.MigrateAsync(); return true; });
            return fixture;
        }

        public Task AddConnectionAsync(string id, string url = "https://example.invalid", string providerId = "YouTrack") =>
            EditAsync(db =>
            {
                db.WorkSourceConnections.Add(new() { Id = id, ProviderId = providerId, BaseUrl = url });
                return Task.CompletedTask;
            });

        public async Task SeedSourceAsync(string sourceId, string connectionId, string url = "https://example.invalid")
        {
            await AddConnectionAsync(connectionId, url);
            await Store.SetAccessTokenAsync(connectionId, connectionId + "-token");
            await AddProjectAsync(sourceId, connectionId);
        }

        public Task AddProjectAsync(string sourceId, string connectionId, string remoteId = "0-1") => EditAsync(db =>
        {
            db.ManagedProjects.Add(new() { SourceId = sourceId, Name = "Finly", WorkSourceConnectionId = connectionId, ExternalProjectId = remoteId });
            db.YouTrackProjectConfigurations.Add(new() { SourceId = sourceId });
            return Task.CompletedTask;
        });

        public Task AddAgentAsync(string sourceId, string agentId, string remoteId,
            bool enabled = true, bool deletionRequested = false, string? storedLogin = null) => EditAsync(db =>
        {
            db.AgentProfiles.Add(new() { Id = agentId, Name = agentId, Instructions = "Test", IsEnabled = enabled,
                DeletionRequestedAt = deletionRequested ? DateTimeOffset.UtcNow : null });
            db.AgentExternalIdentities.Add(new() { AgentId = agentId, SourceId = sourceId, ExternalUserId = remoteId,
                Login = storedLogin ?? "stored-" + remoteId });
            return Task.CompletedTask;
        });

        public async Task<T> ReadAsync<T>(Func<TrailyDbContext, Task<T>> action)
        {
            await using var scope = provider.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<TrailyDbContext>());
        }

        public async Task EditAsync(Func<TrailyDbContext, Task> action)
        {
            await using var scope = provider.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TrailyDbContext>();
            await action(database);
            await database.SaveChangesAsync();
        }

        public Task<List<OperationalIssue>> HistoryAsync() => ReadAsync(db => db.OperationalIssues.AsNoTracking().ToListAsync());

        public Task<IReadOnlyList<WorkItemSyncResult>> SyncAsync() => ReadAsync(db => new WorkItemSynchronizer(Source, db).SyncAsync());

        public async ValueTask DisposeAsync()
        {
            await provider.DisposeAsync();
            client.Dispose();
            handler.Dispose();
            await connection.DisposeAsync();
        }
    }
}
