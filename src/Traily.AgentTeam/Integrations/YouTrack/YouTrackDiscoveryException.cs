namespace Traily.AgentTeam.Integrations.YouTrack;

public sealed class YouTrackDiscoveryException(
    string sourceId,
    string reasonCode,
    Exception innerException)
    : Exception(
        $"Work discovery failed for source '{sourceId}': {reasonCode}.",
        innerException)
{
    public string SourceId { get; } = sourceId;

    public string ReasonCode { get; } = reasonCode;
}