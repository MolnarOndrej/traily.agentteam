using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.WorkSources;

public sealed class WorkSourceAccessService(
    TrailyDbContext database,
    IEnumerable<IWorkSourceAccessCheck> checks,
    OperationalIssueService issueService,
    ILogger<WorkSourceAccessService> logger)
{
    public async Task<IReadOnlyList<OperationalObservation>> CheckAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observations = new List<OperationalObservation>();
        var configurationScope = new OperationalScope(
            "Environment", "OperationalDatabase", "ReadWorkSourceConfiguration");
        List<WorkSourceConnection> connections;
        List<ManagedProject> projects;
        try
        {
            connections = await database.WorkSourceConnections.AsNoTracking()
                .OrderBy(entry => entry.Id).ToListAsync(cancellationToken);
            projects = await database.ManagedProjects.AsNoTracking()
                .OrderBy(entry => entry.SourceId).ToListAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await ObserveAsync(new OperationalObservation(
                configurationScope, OperationalAvailability.Unknown,
                "ConfigurationReadFailed", "Work-source configuration could not be read."));
            return observations;
        }

        await ObserveAsync(new OperationalObservation(
            configurationScope, OperationalAvailability.Available));

        foreach (var connection in connections)
        {
            var connectionScope = new OperationalScope(
                "WorkSourceConnection", connection.Id, "Authenticate");
            var providers = checks.Where(check => string.Equals(
                check.ProviderId, connection.ProviderId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var check = providers.Length == 1 ? providers[0] : null;
            var connectionObservation = check is null
                ? new OperationalObservation(connectionScope, OperationalAvailability.Unknown,
                    "AccessCheckUnavailable", "Exactly one access checker must be registered for this provider.")
                : await RunCheckAsync(connectionScope,
                    () => check.CheckConnectionAsync(connection, cancellationToken));
            await ObserveAsync(connectionObservation);

            foreach (var project in projects.Where(project =>
                project.WorkSourceConnectionId == connection.Id))
            {
                var projectScope = new OperationalScope("WorkSource", project.SourceId, "ReadProject");
                OperationalObservation observation;
                if (string.IsNullOrWhiteSpace(project.ExternalProjectId))
                {
                    observation = new OperationalObservation(projectScope,
                        OperationalAvailability.Unknown, "ProjectNotConfigured",
                        "Configure the stable external project ID explicitly.");
                }
                else if (connectionObservation.Availability != OperationalAvailability.Available)
                {
                    // An unverified connection cannot resolve a previous project failure.
                    observation = new OperationalObservation(projectScope,
                        OperationalAvailability.Unknown, "ConnectionNotVerified",
                        "Project access could not be checked because connection authentication is not verified.");
                }
                else
                {
                    observation = await RunCheckAsync(projectScope,
                        () => check!.CheckProjectAsync(connection, project, cancellationToken));
                }
                await ObserveAsync(observation);
            }
        }

        foreach (var project in projects.Where(project =>
            !connections.Any(connection => connection.Id == project.WorkSourceConnectionId)))
        {
            await ObserveAsync(new OperationalObservation(
                new OperationalScope("WorkSource", project.SourceId, "ReadProject"),
                OperationalAvailability.Unknown, "ConnectionNotConfigured",
                "Configure an explicit work-source connection for this project."));
        }

        return observations;

        async Task<OperationalObservation> RunCheckAsync(
            OperationalScope scope, Func<Task<OperationalObservation>> run)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var observation = await run();
                if (observation.Scope != scope)
                    throw new InvalidOperationException("Access checker returned an unexpected scope.");
                return observation;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Isolate a failed adapter; never log exception text containing request details.
                return new OperationalObservation(scope, OperationalAvailability.Unknown,
                    "AccessCheckFailed", "The access checker could not determine this capability.");
            }
        }

        async Task ObserveAsync(OperationalObservation observation)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observations.Add(observation);
            try
            {
                await issueService.ObserveAsync(observation, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    "Could not persist access observation: {ScopeType} {ScopeId}; " +
                    "{Capability}; {Availability}; {ReasonCode}; {Message}. " +
                    "Stored issue status is unknown. Failure type: {FailureType}.",
                    observation.Scope.Type, observation.Scope.Id, observation.Scope.Capability,
                    observation.Availability, observation.ReasonCode, observation.Message,
                    exception.GetType().Name);
            }
        }
    }
}
