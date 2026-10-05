using System.Net;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Integrations.Http;
using Traily.AgentTeam.Integrations.YouTrack;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.WorkSources;

namespace Traily.AgentTeam.Tests;

public sealed class YouTrackAccessCheckTests
{
    [Fact]
    public async Task AccountAndProjectUseTheConfiguredTokenAndContextPathWithoutAccountStatusPolicy()
    {
        var configuration = Configuration();
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(configuration.AccessToken, request.Headers.Authorization.Parameter);
            Assert.Equal("example.invalid", request.RequestUri!.Host);
            Assert.Contains("application/json", request.Headers.Accept.Select(value => value.MediaType));
            return Task.FromResult(Response(request.RequestUri.AbsolutePath.EndsWith("/me")
                ? "{\"id\":\"2-4\"}"
                : "{\"id\":\"0-1\"}"));
        });
        using var client = new HttpClient(handler);
        var check = new YouTrackWorkSourceAccessCheck(client, () => configuration);

        var account = await check.CheckConnectionAsync(Connection());
        var project = await check.CheckProjectAsync(Connection(), Project());

        Assert.Equal(OperationalAvailability.Available, account.Availability);
        Assert.Equal(new OperationalScope("WorkSourceConnection", "connection", "Authenticate"), account.Scope);
        Assert.Equal(OperationalAvailability.Available, project.Availability);
        Assert.Equal(new OperationalScope("WorkSource", "source", "ReadProject"), project.Scope);
        Assert.Null(account.ReasonCode);
        Assert.Null(project.HttpStatusCode);
        Assert.Equal(new[] { "/youtrack/api/users/me", "/youtrack/api/admin/projects/0-1" }, handler.Paths);
    }

    [Theory]
    [InlineData(401, OperationalAvailability.Unavailable, "AuthenticationFailed")]
    [InlineData(403, OperationalAvailability.Unavailable, "AccessDenied")]
    [InlineData(404, OperationalAvailability.Unknown, "ResourceInaccessible")]
    [InlineData(429, OperationalAvailability.Unknown, "RateLimited")]
    [InlineData(503, OperationalAvailability.Unknown, "RemoteServiceFailure")]
    [InlineData(302, OperationalAvailability.Unknown, "RequestRejected")]
    [InlineData(400, OperationalAvailability.Unknown, "RequestRejected")]
    public async Task FailedResponsesPreserveStatusAndDoNotExposeResponseBody(
        int status, OperationalAvailability availability, string reason)
    {
        using var handler = new Handler((_, _) => Task.FromResult(
            Response("secret-response-marker", (HttpStatusCode)status)));
        using var client = new HttpClient(handler);
        var check = new YouTrackWorkSourceAccessCheck(client, Configuration);

        var observation = await check.CheckProjectAsync(Connection(), Project());

        Assert.Equal(availability, observation.Availability);
        Assert.Equal(reason, observation.ReasonCode);
        Assert.Equal(status, observation.HttpStatusCode);
        Assert.DoesNotContain("secret-response-marker", observation.Message!);
        Assert.DoesNotContain("fake-token-marker", observation.Message!);
    }

    [Theory]
    [InlineData("invalid-private-response")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"id\":1}")]
    [InlineData("{\"id\":\"0-99\"}")]
    public async Task MalformedOrWrongProjectResponseCannotEstablishAccess(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(body)));
        using var client = new HttpClient(handler);
        var observation = await new YouTrackWorkSourceAccessCheck(client, Configuration)
            .CheckProjectAsync(Connection(), Project());
        Assert.Equal(OperationalAvailability.Unknown, observation.Availability);
        Assert.Equal("InvalidAccessResponse", observation.ReasonCode);
        Assert.DoesNotContain(body, observation.Message!);
    }

    [Theory]
    [InlineData("https://other.invalid/youtrack", "ConnectionConfigurationUnavailable")]
    [InlineData("http://example.invalid/youtrack", "InvalidConnectionConfiguration")]
    [InlineData("https://user:secret@example.invalid/youtrack", "InvalidConnectionConfiguration")]
    [InlineData("https://example.invalid/youtrack?token=secret", "InvalidConnectionConfiguration")]
    public async Task TokenIsNeverSentToAnUnconfiguredOrInvalidService(string url, string reason)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Unexpected request"));
        using var client = new HttpClient(handler);
        var connection = Connection();
        connection.BaseUrl = url;
        var observation = await new YouTrackWorkSourceAccessCheck(client, Configuration)
            .CheckConnectionAsync(connection);
        Assert.Equal(reason, observation.ReasonCode);
        Assert.Empty(handler.Paths);
        Assert.DoesNotContain(url, observation.Message!);
    }

    [Fact]
    public async Task InvalidConfigurationProducesASafeObservationWithoutRequests()
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Unexpected request"));
        using var client = new HttpClient(handler);
        var check = new YouTrackWorkSourceAccessCheck(client,
            () => throw new YouTrackConfigurationException("private-configuration-marker"));
        var observation = await check.CheckConnectionAsync(Connection());
        Assert.Equal("InvalidYouTrackConfiguration", observation.ReasonCode);
        Assert.DoesNotContain("private-configuration-marker", observation.Message!);
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData("STEPI")]
    [InlineData("../users/me")]
    [InlineData("")]
    public async Task ProjectMustHaveAnExplicitOpaqueId(string id)
    {
        using var handler = new Handler((_, _) => throw new InvalidOperationException("Unexpected request"));
        using var client = new HttpClient(handler);
        var project = Project();
        project.ExternalProjectId = id;
        var observation = await new YouTrackWorkSourceAccessCheck(client, Configuration)
            .CheckProjectAsync(Connection(), project);
        Assert.Equal("InvalidProjectId", observation.ReasonCode);
        Assert.Empty(handler.Paths);
    }

    [Theory]
    [InlineData(null, "ConnectionFailed", OperationalAvailability.Unknown)]
    [InlineData(403, "AccessDenied", OperationalAvailability.Unavailable)]
    public async Task HttpExceptionRetainsItsStatusWithoutRawDiagnostics(
        int? status, string reason, OperationalAvailability availability)
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException(
            "private-exception-marker", null, status is { } value ? (HttpStatusCode)value : null));
        using var client = new HttpClient(handler);
        var observation = await new YouTrackWorkSourceAccessCheck(client, Configuration)
            .CheckConnectionAsync(Connection());
        Assert.Equal(reason, observation.ReasonCode);
        Assert.Equal(status, observation.HttpStatusCode);
        Assert.Equal(availability, observation.Availability);
        Assert.DoesNotContain("private-exception-marker", observation.Message!);
    }

    [Fact]
    public async Task IndependentTimeoutIsUnknownButCallerCancellationPropagates()
    {
        using var handler = new Handler((_, _) => throw new OperationCanceledException("private-marker"));
        using var client = new HttpClient(handler);
        var reads = 0;
        var check = new YouTrackWorkSourceAccessCheck(client, () => { reads++; return Configuration(); });
        var observation = await check.CheckConnectionAsync(Connection());
        Assert.Equal("RequestTimedOut", observation.ReasonCode);
        Assert.Equal(OperationalAvailability.Unknown, observation.Availability);
        Assert.DoesNotContain("private-marker", observation.Message!);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            check.CheckConnectionAsync(Connection(), cancellation.Token));
        Assert.Equal(1, reads);
        Assert.Single(handler.Paths);
    }

    [Fact]
    public async Task InFlightCallerCancellationIsNotClassifiedAsATimeout()
    {
        using var cancellation = new CancellationTokenSource();
        using var handler = new Handler((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Unreachable");
        });
        using var client = new HttpClient(handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new YouTrackWorkSourceAccessCheck(client, Configuration)
                .CheckConnectionAsync(Connection(), cancellation.Token));
    }

    [Fact]
    public void GenericHttpFailureCanBeRefinedForAnotherProviderWithoutChangingItsStatus()
    {
        var failure = HttpFailureClassifier.Classify(403) with
        {
            Kind = HttpFailureKind.RateLimited,
            Message = "The provider reported a quota limit."
        };
        var observation = failure.ToObservation(new("GitRepository", "repo", "ReadRemote"),
            OperationalAvailability.Unknown, "Git provider read");
        Assert.Equal("RateLimited", observation.ReasonCode);
        Assert.Equal(403, observation.HttpStatusCode);
        Assert.Equal("GitRepository", observation.Scope.Type);
        Assert.Contains("Git provider read", observation.Message!);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(299)]
    [InlineData(99)]
    [InlineData(600)]
    public void HttpFailureClassifierRejectsSuccessfulAndInvalidStatusCodes(int status) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => HttpFailureClassifier.Classify(status));

    private static YouTrackConfiguration Configuration() => YouTrackConfiguration.FromEnvironment(
        new Dictionary<string, string?>
        {
            [YouTrackConfiguration.BaseUrlEnvironmentVariable] = "https://example.invalid/youtrack/",
            [YouTrackConfiguration.TokenEnvironmentVariable] = "fake-token-marker",
            [YouTrackConfiguration.DiscoveryQueryEnvironmentVariable] = "project: STEPI",
            [YouTrackConfiguration.SourceIdEnvironmentVariable] = "source"
        }.GetValueOrDefault);

    private static WorkSourceConnection Connection() => new()
    {
        Id = "connection", ProviderId = "YouTrack", BaseUrl = "https://example.invalid/youtrack/"
    };

    private static ManagedProject Project() => new()
    {
        SourceId = "source", Name = "Project", WorkSourceConnectionId = "connection", ExternalProjectId = "0-1"
    };

    private static HttpResponseMessage Response(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return send(request, token);
        }
    }
}
