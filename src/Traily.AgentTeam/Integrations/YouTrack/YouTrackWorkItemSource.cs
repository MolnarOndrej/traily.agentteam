using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Integrations.YouTrack;

public sealed class YouTrackWorkItemSource
    : IWorkItemReader,
      IWorkItemDiscovery
{
    private readonly HttpClient _httpClient;
    private readonly YouTrackConfiguration _configuration;

    public YouTrackWorkItemSource(
        HttpClient httpClient,
        YouTrackConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(configuration);

        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<WorkItem> GetRequiredAsync(
        string workItemId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);

        var encodedWorkItemId =
            Uri.EscapeDataString(workItemId);

        using var request = CreateGetRequest(
            $"api/issues/{encodedWorkItemId}" +
            "?fields=idReadable,summary,description");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new KeyNotFoundException(
                $"YouTrack work item '{workItemId}' was not found.");
        }

        response.EnsureSuccessStatusCode();

        var issue = await response.Content
            .ReadFromJsonAsync<YouTrackIssueResponse>(
                cancellationToken);

        if (issue is null ||
            string.IsNullOrWhiteSpace(issue.Id) ||
            string.IsNullOrWhiteSpace(issue.Summary))
        {
            throw new InvalidDataException(
                $"YouTrack returned incomplete data for " +
                $"work item '{workItemId}'.");
        }

        return new WorkItem(
            issue.Id,
            issue.Summary,
            issue.Description ?? string.Empty);
    }

    public async Task<IReadOnlyList<DiscoveredWorkItem>>
        FindReadyAsync(
            CancellationToken cancellationToken = default)
    {
        var encodedQuery = Uri.EscapeDataString(
            _configuration.DiscoveryQuery);

        var encodedWorkflowStateField =
            Uri.EscapeDataString(
                _configuration.WorkflowStateField);

        var encodedAssigneeField =
            Uri.EscapeDataString(
                _configuration.AssigneeField);

        var requestUri =
            "api/issues" +
            $"?query={encodedQuery}" +
            "&$top=100" +
            "&fields=idReadable,summary,updated," +
            "customFields(name,value(name,login))" +
            $"&customFields={encodedWorkflowStateField}" +
            $"&customFields={encodedAssigneeField}";

        using var request = CreateGetRequest(requestUri);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var issues = await response.Content
            .ReadFromJsonAsync<YouTrackIssueResponse[]>(
                cancellationToken);

        if (issues is null)
        {
            throw new InvalidDataException(
                "YouTrack returned an invalid issue collection.");
        }

        return issues
            .Select(MapDiscoveredWorkItem)
            .ToArray();
    }

    private HttpRequestMessage CreateGetRequest(
        string requestUri)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            requestUri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _configuration.AccessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        return request;
    }

    private DiscoveredWorkItem MapDiscoveredWorkItem(
        YouTrackIssueResponse issue)
    {
        if (string.IsNullOrWhiteSpace(issue.Id) ||
            string.IsNullOrWhiteSpace(issue.Summary) ||
            issue.Updated is null)
        {
            throw new InvalidDataException(
                "YouTrack returned incomplete discovery data.");
        }

        var state = issue.CustomFields?
            .SingleOrDefault(field =>
                string.Equals(
                    field.Name,
                    _configuration.WorkflowStateField,
                    StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Name;

        var assigneeId = issue.CustomFields?
            .SingleOrDefault(field =>
                string.Equals(
                    field.Name,
                    _configuration.AssigneeField,
                    StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Login;

        if (string.IsNullOrWhiteSpace(state) ||
            string.IsNullOrWhiteSpace(assigneeId))
        {
            throw new InvalidDataException(
                $"YouTrack work item '{issue.Id}' has incomplete " +
                "state or assignee data.");
        }

        return new DiscoveredWorkItem(
            issue.Id,
            issue.Summary,
            state,
            assigneeId,
            DateTimeOffset.FromUnixTimeMilliseconds(
                issue.Updated.Value));
    }

    private sealed record YouTrackIssueResponse(
        [property: JsonPropertyName("idReadable")]
        string? Id,
        [property: JsonPropertyName("summary")]
        string? Summary,
        [property: JsonPropertyName("description")]
        string? Description,
        [property: JsonPropertyName("updated")]
        long? Updated,
        [property: JsonPropertyName("customFields")]
        YouTrackCustomFieldResponse[]? CustomFields);

    private sealed record YouTrackCustomFieldResponse(
        [property: JsonPropertyName("name")]
        string? Name,
        [property: JsonPropertyName("value")]
        YouTrackFieldValueResponse? Value);

    private sealed record YouTrackFieldValueResponse(
        [property: JsonPropertyName("name")]
        string? Name,
        [property: JsonPropertyName("login")]
        string? Login);
}