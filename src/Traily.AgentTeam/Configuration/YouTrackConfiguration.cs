namespace Traily.AgentTeam.Configuration;

public sealed class YouTrackConfiguration
{
    public YouTrackConfiguration(
        Uri baseAddress,
        string accessToken,
        string sourceId,
        string externalProjectId,
        string discoveryQuery,
        string workflowStateField,
        string assigneeField)
    {
        BaseAddress = baseAddress;
        AccessToken = accessToken;
        DiscoveryQuery = discoveryQuery;
        WorkflowStateField = workflowStateField;
        AssigneeField = assigneeField;
        SourceId = sourceId;
        ExternalProjectId = externalProjectId;
    }

    public Uri BaseAddress { get; }

    public string AccessToken { get; }

    public string DiscoveryQuery { get; }

    public string WorkflowStateField { get; }

    public string AssigneeField { get; }

    public string SourceId { get; }

    public string ExternalProjectId { get; }
}
