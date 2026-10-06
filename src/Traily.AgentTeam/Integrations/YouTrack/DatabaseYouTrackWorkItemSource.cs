using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Integrations.YouTrack;

public sealed class DatabaseYouTrackWorkItemSource(
    IServiceScopeFactory scopeFactory, YouTrackConfigurationStore configurations,
    HttpClient httpClient, OperationalIssueService issues,
    ILogger<ObservedYouTrackDiscovery> observationLogger,
    ILogger<DatabaseYouTrackWorkItemSource> logger) : IWorkItemDiscovery, IWorkItemReader
{
    public async Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrailyDbContext>();
        var sourceIds = await database.ManagedProjects.AsNoTracking()
            .Where(project => project.WorkSourceConnection.ProviderId.ToLower() == "youtrack")
            .OrderBy(project => project.SourceId).Select(project => project.SourceId)
            .ToListAsync(cancellationToken);
        var items = new List<DiscoveredWorkItem>();
        foreach (var sourceId in sourceIds)
        {
            try
            {
                var settings = await configurations.GetSourceAsync(sourceId, cancellationToken);
                var identities = await database.AgentExternalIdentities.AsNoTracking()
                    .Where(identity => identity.SourceId == sourceId && identity.Agent.IsEnabled &&
                        identity.Agent.DeletionRequestedAt == null)
                    .OrderBy(identity => identity.ExternalUserId).ToListAsync(cancellationToken);
                await ObserveConfigurationAsync(sourceId, null, cancellationToken);

                // No active assignment account means no discovery request, not verified remote health.
                if (identities.Count == 0)
                    continue;
                var discovery = new ProjectDiscovery(this, settings, identities);
                var observed = new ObservedYouTrackDiscovery(discovery, sourceId, issues, observationLogger);
                items.AddRange(await observed.FindReadyAsync(cancellationToken));
            }
            catch (YouTrackConfigurationException exception)
            {
                await ObserveConfigurationAsync(sourceId, exception.Message, cancellationToken);
            }
            catch (YouTrackDiscoveryException exception)
            {
                logger.LogDebug("Discovery unavailable for {SourceId}: {ReasonCode}.", sourceId, exception.ReasonCode);
            }
        }
        return items;
    }

    public async Task<WorkItem> GetRequiredAsync(
        string sourceId, string externalWorkItemId, CancellationToken cancellationToken = default)
    {
        var settings = await configurations.GetSourceAsync(sourceId, cancellationToken);
        return await CreateSource(settings, string.Empty)
            .GetRequiredAsync(sourceId, externalWorkItemId, cancellationToken);
    }

    private YouTrackWorkItemSource CreateSource(YouTrackSourceSettings settings, string query) =>
        new(httpClient, YouTrackConfiguration.FromDatabase(
            settings.Connection.BaseAddress, settings.Connection.AccessToken,
            settings.Project.SourceId, settings.Project.ExternalProjectId, query,
            settings.Configuration.WorkflowStateField, settings.Configuration.AssigneeField));

    private async Task<IReadOnlyList<DiscoveredWorkItem>> DiscoverProjectAsync(
        YouTrackSourceSettings settings, IReadOnlyList<AgentExternalIdentity> identities,
        CancellationToken cancellationToken)
    {
        var project = await ReadIdentityAsync(settings.Connection,
            $"api/admin/projects/{Uri.EscapeDataString(settings.Project.ExternalProjectId)}?fields=id,shortName",
            settings.Project.ExternalProjectId, cancellationToken);
        if (string.IsNullOrWhiteSpace(project.ShortName))
            throw new InvalidDataException("YouTrack returned an incomplete project identity.");

        var items = new Dictionary<string, DiscoveredWorkItem>(StringComparer.Ordinal);
        foreach (var identity in identities)
        {
            // Resolve the current login from the durable ID so an account rename cannot silently hide tickets.
            var user = await ReadIdentityAsync(settings.Connection,
                $"api/users/{Uri.EscapeDataString(identity.ExternalUserId)}?fields=id,login",
                identity.ExternalUserId, cancellationToken);
            if (string.IsNullOrWhiteSpace(user.Login))
                throw new InvalidDataException("YouTrack returned an incomplete assignment account.");
            var query = YouTrackConfigurationStore.CreateDiscoveryQuery(
                settings.Configuration, project.ShortName, user.Login);
            foreach (var item in await CreateSource(settings, query).FindReadyAsync(cancellationToken))
            {
                // A query template is configuration, not an identity boundary.
                if (item.ExternalAssigneeId == identity.ExternalUserId)
                    items.TryAdd(item.ExternalWorkItemId, item);
            }
        }
        return items.Values.ToArray();
    }

    private async Task<RemoteIdentity> ReadIdentityAsync(
        YouTrackConnectionSettings connection, string path, string expectedId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(connection.BaseAddress, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.AccessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var identity = await response.Content.ReadFromJsonAsync<RemoteIdentity>(cancellationToken);
        if (identity is null || identity.Id != expectedId)
            throw new InvalidDataException("YouTrack returned a mismatched resource identity.");
        return identity;
    }

    private async Task ObserveConfigurationAsync(
        string sourceId, string? diagnostic, CancellationToken cancellationToken)
    {
        try
        {
            await issues.ObserveAsync(new OperationalObservation(
                new("Configuration", sourceId, "ConfigureDiscovery"),
                diagnostic is null ? OperationalAvailability.Available : OperationalAvailability.Unavailable,
                diagnostic is null ? null : "InvalidConfiguration", diagnostic), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError("Could not persist configuration observation for {SourceId}. Failure type: {FailureType}.",
                sourceId, exception.GetType().Name);
        }
    }

    private sealed class ProjectDiscovery(
        DatabaseYouTrackWorkItemSource owner, YouTrackSourceSettings settings,
        IReadOnlyList<AgentExternalIdentity> identities) : IWorkItemDiscovery
    {
        public Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(CancellationToken cancellationToken = default) =>
            owner.DiscoverProjectAsync(settings, identities, cancellationToken);
    }

    private sealed record RemoteIdentity(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("shortName")] string? ShortName,
        [property: JsonPropertyName("login")] string? Login);
}
