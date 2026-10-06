using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Integrations.Http;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.WorkSources;

namespace Traily.AgentTeam.Integrations.YouTrack;

public sealed class YouTrackWorkSourceAccessCheck : IWorkSourceAccessCheck
{
    private readonly HttpClient httpClient;
    private readonly Func<WorkSourceConnection, CancellationToken, Task<YouTrackConnectionSettings>> getConfiguration;

    public YouTrackWorkSourceAccessCheck(HttpClient httpClient, YouTrackConfigurationStore configurations)
    {
        this.httpClient = httpClient;
        getConfiguration = (connection, cancellationToken) =>
            configurations.GetConnectionAsync(connection.Id, cancellationToken);
    }

    // Retained for existing isolated adapter fixtures; production uses the database store.
    public YouTrackWorkSourceAccessCheck(HttpClient httpClient, Func<YouTrackConfiguration> configuration)
    {
        this.httpClient = httpClient;
        getConfiguration = (connection, _) =>
        {
            var settings = configuration();
            return Task.FromResult(new YouTrackConnectionSettings(
                connection.Id, settings.BaseAddress, settings.AccessToken));
        };
    }

    public string ProviderId => "YouTrack";

    public async Task<OperationalObservation> CheckConnectionAsync(
        WorkSourceConnection connection,
        CancellationToken cancellationToken = default)
    {
        var scope = new OperationalScope("WorkSourceConnection", connection.Id, "Authenticate");
        return await ReadAsync(connection, scope, "api/users/me?fields=id",
            root => HasId(root)
                ? new OperationalObservation(scope, OperationalAvailability.Available)
                : InvalidResponse(scope), cancellationToken);
    }

    public async Task<OperationalObservation> CheckProjectAsync(
        WorkSourceConnection connection,
        ManagedProject project,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = new OperationalScope("WorkSource", project.SourceId, "ReadProject");
        // Require the opaque database ID, rather than a mutable project key or a search query.
        if (project.ExternalProjectId is null || project.ExternalProjectId.Length > 200 ||
            !Regex.IsMatch(project.ExternalProjectId, @"\A[0-9]+-[0-9]+\z"))
            return new OperationalObservation(scope, OperationalAvailability.Unknown,
                "InvalidProjectId", "Configure the stable YouTrack project database ID (digits-digits).");

        return await ReadAsync(connection, scope,
            $"api/admin/projects/{Uri.EscapeDataString(project.ExternalProjectId)}?fields=id",
            root => HasId(root) && root.GetProperty("id").GetString() == project.ExternalProjectId
                ? new OperationalObservation(scope, OperationalAvailability.Available)
                : InvalidResponse(scope), cancellationToken);
    }

    private async Task<OperationalObservation> ReadAsync(
        WorkSourceConnection connection,
        OperationalScope scope,
        string path,
        Func<JsonElement, OperationalObservation> interpret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        YouTrackConnectionSettings configuration;
        try
        {
            configuration = await getConfiguration(connection, cancellationToken);
        }
        catch (YouTrackConfigurationException)
        {
            return new OperationalObservation(scope, OperationalAvailability.Unavailable,
                "InvalidYouTrackConfiguration", "This YouTrack connection configuration is invalid.");
        }

        if (!Uri.TryCreate(connection.BaseUrl.Trim().TrimEnd('/') + "/",
                UriKind.Absolute, out var baseAddress) ||
            baseAddress.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(baseAddress.UserInfo) ||
            !string.IsNullOrEmpty(baseAddress.Query) ||
            !string.IsNullOrEmpty(baseAddress.Fragment))
            return new OperationalObservation(scope, OperationalAvailability.Unavailable,
                "InvalidConnectionConfiguration", "Configure an HTTPS service URL without credentials, query, or fragment.");

        // Reject a stale/mismatched target; credentials are bound to the selected connection.
        if (baseAddress != configuration.BaseAddress)
            return new OperationalObservation(scope, OperationalAvailability.Unknown,
                "ConnectionConfigurationUnavailable",
                "This connection does not match its loaded YouTrack configuration.");

        try
        {
            // Use the same configured service URL and token as work-item discovery.
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(configuration.BaseAddress, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.AccessToken);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Classify(scope, (int)response.StatusCode);

            using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(content,
                cancellationToken: cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return interpret(document.RootElement);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return HttpFailureClassifier.TimedOut().ToObservation(
                scope, OperationalAvailability.Unknown, "YouTrack access check");
        }
        catch (HttpRequestException exception)
        {
            return Classify(scope, exception.StatusCode is { } status ? (int)status : null);
        }
        catch (JsonException)
        {
            return InvalidResponse(scope);
        }
    }

    private static OperationalObservation Classify(OperationalScope scope, int? status)
    {
        var failure = HttpFailureClassifier.Classify(status);
        // Rejected credentials/access are known failures. Other failed probes do
        // not establish whether the account has the requested permission.
        var availability = failure.Kind is
            HttpFailureKind.AuthenticationFailed or HttpFailureKind.AccessDenied
                ? OperationalAvailability.Unavailable
                : OperationalAvailability.Unknown;
        return failure.ToObservation(scope, availability, "YouTrack access check");
    }

    private static bool HasId(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(id.GetString());

    private static OperationalObservation InvalidResponse(OperationalScope scope) =>
        new(scope, OperationalAvailability.Unknown, "InvalidAccessResponse",
            "The access response could not be parsed or validated; the capability is not verified.");
}
