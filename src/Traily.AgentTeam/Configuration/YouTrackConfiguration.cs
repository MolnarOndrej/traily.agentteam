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

    public const string SourceIdEnvironmentVariable =
        "TRAILY_YOUTRACK_SOURCE_ID";

    private const string DefaultWorkflowStateField = "Stage";
    private const string DefaultAssigneeField = "Assignee";

    private YouTrackConfiguration(
        Uri baseAddress,
        string accessToken,
        string discoveryQuery,
        string workflowStateField,
        string assigneeField,
        string sourceId)
    {
        BaseAddress = baseAddress;
        AccessToken = accessToken;
        DiscoveryQuery = discoveryQuery;
        WorkflowStateField = workflowStateField;
        AssigneeField = assigneeField;
        SourceId = sourceId;
    }

    public Uri BaseAddress { get; }

    public string AccessToken { get; }

    public string DiscoveryQuery { get; }

    public string WorkflowStateField { get; }

    public string AssigneeField { get; }

    public string SourceId { get; }

    public static YouTrackConfiguration FromEnvironment(
        Func<string, string?>? readValue = null)
    {
        readValue ??= Environment.GetEnvironmentVariable;
        var errors = new List<string>();
        var baseUrl = readValue(BaseUrlEnvironmentVariable);
        Uri? baseAddress = null;

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            errors.Add($"Configure {BaseUrlEnvironmentVariable}.");
        }
        else
        {
            var normalizedBaseUrl =
                baseUrl.Trim().TrimEnd('/') + "/";

            if (!Uri.TryCreate(
                    normalizedBaseUrl,
                    UriKind.Absolute,
                    out baseAddress) ||
                baseAddress.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add($"{BaseUrlEnvironmentVariable} must be an HTTPS URL.");
            }
        }

        var accessToken = readValue(TokenEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            errors.Add($"Configure {TokenEnvironmentVariable}.");
        }

        var discoveryQuery = readValue(DiscoveryQueryEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(discoveryQuery))
        {
            errors.Add($"Configure {DiscoveryQueryEnvironmentVariable}.");
        }

        var sourceId =
            readValue(SourceIdEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(sourceId))
        {
            errors.Add($"Configure {SourceIdEnvironmentVariable}.");
        }
        else if (sourceId.Trim().Length > 100)
        {
            errors.Add($"{SourceIdEnvironmentVariable} must not exceed 100 characters.");
        }

        if (errors.Count > 0)
        {
            throw new YouTrackConfigurationException(string.Join(" ", errors));
        }

        var workflowStateField = ResolveOptionalValue(
            readValue,
            WorkflowStateFieldEnvironmentVariable,
            DefaultWorkflowStateField);

        var assigneeField = ResolveOptionalValue(
            readValue,
            AssigneeFieldEnvironmentVariable,
            DefaultAssigneeField);

        return new YouTrackConfiguration(
            baseAddress!,
            accessToken!.Trim(),
            discoveryQuery!.Trim(),
            workflowStateField,
            assigneeField,
            sourceId!.Trim());
    }

    private static string ResolveOptionalValue(
        Func<string, string?> readValue,
        string environmentVariable,
        string defaultValue)
    {
        var configuredValue =
            readValue(
                environmentVariable);

        return string.IsNullOrWhiteSpace(configuredValue)
            ? defaultValue
            : configuredValue.Trim();
    }
}
