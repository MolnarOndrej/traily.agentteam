namespace Traily.AgentTeam.Integrations.YouTrack;

public sealed class YouTrackConnectionConfiguration
{
    public string ConnectionId { get; set; } = string.Empty;
    public string ProtectedAccessToken { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
