namespace Traily.AgentTeam.Configuration;

public sealed class YouTrackConfiguration
{
    public const string BaseUrlEnvironmentVariable =
        "TRAILY_YOUTRACK_BASE_URL";

    public const string TokenEnvironmentVariable =
        "TRAILY_YOUTRACK_TOKEN";

    public const string DiscoveryQueryEnvironmentVariable =
        "TRAILY_YOUTRACK_DISCOVERY_QUERY";

    public const string WorkflowStateFieldEnvironmentVariable =
        "TRAILY_YOUTRACK_WORKFLOW_STATE_FIELD";

    public const string AssigneeFieldEnvironmentVariable =
        "TRAILY_YOUTRACK_ASSIGNEE_FIELD";

    private const string DefaultWorkflowStateField = "Stage";
    private const string DefaultAssigneeField = "Assignee";

    private YouTrackConfiguration(
        Uri baseAddress,
        string accessToken,
        string discoveryQuery,
        string workflowStateField,
        string assigneeField)
    {
        BaseAddress = baseAddress;
        AccessToken = accessToken;
        DiscoveryQuery = discoveryQuery;
        WorkflowStateField = workflowStateField;
        AssigneeField = assigneeField;
    }

    public Uri BaseAddress { get; }

    public string AccessToken { get; }

    public string DiscoveryQuery { get; }

    public string WorkflowStateField { get; }

    public string AssigneeField { get; }

    public static YouTrackConfiguration FromEnvironment()
    {
        var baseUrl = Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                $"Configure {BaseUrlEnvironmentVariable}.");
        }

        var normalizedBaseUrl =
            baseUrl.Trim().TrimEnd('/') + "/";

        if (!Uri.TryCreate(
                normalizedBaseUrl,
                UriKind.Absolute,
                out var baseAddress) ||
            baseAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"{BaseUrlEnvironmentVariable} must be an HTTPS URL.");
        }

        var accessToken = Environment.GetEnvironmentVariable(TokenEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                $"Configure {TokenEnvironmentVariable}.");
        }

        var discoveryQuery = Environment.GetEnvironmentVariable(DiscoveryQueryEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(discoveryQuery))
        {
            throw new InvalidOperationException(
                $"Configure {DiscoveryQueryEnvironmentVariable}.");
        }

        var workflowStateField = ResolveOptionalValue(
            WorkflowStateFieldEnvironmentVariable,
            DefaultWorkflowStateField);

        var assigneeField = ResolveOptionalValue(
            AssigneeFieldEnvironmentVariable,
            DefaultAssigneeField);

        return new YouTrackConfiguration(
            baseAddress,
            accessToken.Trim(),
            discoveryQuery.Trim(),
            workflowStateField,
            assigneeField);
    }

    private static string ResolveOptionalValue(
        string environmentVariable,
        string defaultValue)
    {
        var configuredValue =
            Environment.GetEnvironmentVariable(
                environmentVariable);

        return string.IsNullOrWhiteSpace(configuredValue)
            ? defaultValue
            : configuredValue.Trim();
    }
}