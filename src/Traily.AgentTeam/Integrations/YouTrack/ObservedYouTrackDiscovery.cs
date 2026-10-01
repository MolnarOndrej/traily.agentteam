using System.Text.Json;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Integrations.YouTrack;

public sealed class ObservedYouTrackDiscovery(
    IWorkItemDiscovery inner,
    string sourceId,
    OperationalIssueService issueService,
    ILogger<ObservedYouTrackDiscovery> logger)
    : IWorkItemDiscovery
{
    private readonly OperationalScope _scope =
        new("WorkSource", sourceId, "DiscoverWorkItems");

    public async Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DiscoveredWorkItem> items;

        try
        {
            items = await inner.FindReadyAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                HttpRequestException or
                OperationCanceledException or
                InvalidDataException or
                JsonException)
        {
            var observation = Classify(exception);

            await TryObserveAsync(
                observation,
                cancellationToken);

            throw new YouTrackDiscoveryException(
                sourceId,
                observation.ReasonCode!,
                exception);
        }

        // Resolve immediately after successful discovery.
        // A later queue/database failure is a separate problem.
        await TryObserveAsync(
            new OperationalObservation(
                _scope,
                OperationalAvailability.Available),
            cancellationToken);

        return items;
    }

    private OperationalObservation Classify(Exception exception)
    {
        if (exception is HttpRequestException http)
        {
            var status = http.StatusCode is { } value
                ? (int?)value
                : null;

            var details = status switch
            {
                401 => (
                    OperationalAvailability.Unavailable,
                    "AuthenticationFailed",
                    "YouTrack rejected the configured credentials."),
                403 => (
                    OperationalAvailability.Unavailable,
                    "AccessDenied",
                    "YouTrack denied the discovery request."),
                404 => (
                    OperationalAvailability.Unavailable,
                    "DiscoveryEndpointUnavailable",
                    "The configured discovery endpoint was not accessible."),
                429 => (
                    OperationalAvailability.Unavailable,
                    "RateLimited",
                    "YouTrack rate-limited the discovery request."),
                >= 500 => (
                    OperationalAvailability.Unavailable,
                    "RemoteServiceFailure",
                    "YouTrack returned a server error."),
                null => (
                    OperationalAvailability.Unknown,
                    "ConnectionFailed",
                    "The discovery request could not reach YouTrack."),
                _ => (
                    OperationalAvailability.Unavailable,
                    "RequestRejected",
                    "YouTrack rejected the discovery request.")
            };

            return new OperationalObservation(
                _scope,
                details.Item1,
                details.Item2,
                details.Item3,
                status);
        }

        if (exception is OperationCanceledException)
        {
            return new OperationalObservation(
                _scope,
                OperationalAvailability.Unknown,
                "RequestTimedOut",
                "Discovery timed out or was canceled independently of shutdown.");
        }

        return new OperationalObservation(
            _scope,
            OperationalAvailability.Unavailable,
            "InvalidDiscoveryResponse",
            "The discovery response could not be parsed or validated.");
    }

    private async Task TryObserveAsync(
        OperationalObservation observation,
        CancellationToken cancellationToken)
    {
        try
        {
            await issueService.ObserveAsync(
                observation,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Keep console reporting available when the issue store fails.
            // Never replace the original discovery outcome with this failure.
            logger.LogError(
                "Could not persist discovery observation for " +
                "{SourceId}: {Availability}; {ReasonCode}; {Message}. " +
                "Persistence failure type: {FailureType}.",
                sourceId,
                observation.Availability,
                observation.ReasonCode,
                observation.Message,
                exception.GetType().Name);
        }
    }
}