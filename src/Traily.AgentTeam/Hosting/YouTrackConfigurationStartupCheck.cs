using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Operations;

namespace Traily.AgentTeam.Hosting;

public sealed class YouTrackConfigurationStartupCheck(
    IServiceProvider services,
    OperationalIssueService issueService,
    ILogger<YouTrackConfigurationStartupCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // This identifies the single configured integration, even without a SourceId.
        // Configuration recovery cannot resolve remote discovery/access issues.
        var scope = new OperationalScope("Configuration", "YouTrack", "ConfigureDiscovery");
        try
        {
            // Resolve here so invalid settings can be reported after the retained issues.
            // A valid singleton is reused by subsequent integration consumers.
            _ = services.GetRequiredService<YouTrackConfiguration>();
        }
        catch (YouTrackConfigurationException exception)
        {
            logger.LogError("YouTrack configuration is invalid. {Diagnostic}", exception.Message);
            await TryObserveAsync(new OperationalObservation(
                scope, OperationalAvailability.Unavailable,
                "InvalidConfiguration", exception.Message), cancellationToken);

            // Invalid settings stop startup even when the issue store is unavailable.
            throw;
        }

        await TryObserveAsync(new OperationalObservation(
            scope, OperationalAvailability.Available), cancellationToken);
    }

    private async Task TryObserveAsync(
        OperationalObservation observation,
        CancellationToken cancellationToken)
    {
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
                "Could not persist the YouTrack configuration observation. " +
                "Stored configuration issue status is unknown. Failure type: {FailureType}.",
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
