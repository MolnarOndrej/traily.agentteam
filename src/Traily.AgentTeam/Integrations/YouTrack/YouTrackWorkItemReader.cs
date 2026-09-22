using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Integrations.YouTrack;

public sealed class YouTrackWorkItemReader
    : IWorkItemReader
{
    private readonly HttpClient _httpClient;
    private readonly YouTrackConfiguration _configuration;

    public YouTrackWorkItemReader(
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

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/issues/{encodedWorkItemId}" +
            "?fields=idReadable,summary,description");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                _configuration.AccessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

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

    private sealed record YouTrackIssueResponse(
        [property: JsonPropertyName("idReadable")]
        string? Id,
        [property: JsonPropertyName("summary")]
        string? Summary,
        [property: JsonPropertyName("description")]
        string? Description);
}