namespace Traily.AgentTeam.Configuration;

public sealed class YouTrackConfigurationException(string message)
    : InvalidOperationException(message);
