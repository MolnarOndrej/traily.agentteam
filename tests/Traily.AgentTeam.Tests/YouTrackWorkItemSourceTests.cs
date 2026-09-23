using System.Net;
using System.Text;
using System.Text.Json;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Integrations.YouTrack;

namespace Traily.AgentTeam.Tests;

public sealed class YouTrackWorkItemSourceTests
{
    [Fact]
    public async Task DiscoveryReadsBeyondFirstPage()
    {
        var names = new[]
        {
            YouTrackConfiguration.BaseUrlEnvironmentVariable,
            YouTrackConfiguration.TokenEnvironmentVariable,
            YouTrackConfiguration.DiscoveryQueryEnvironmentVariable,
            YouTrackConfiguration.SourceIdEnvironmentVariable,
            YouTrackConfiguration.WorkflowStateFieldEnvironmentVariable,
            YouTrackConfiguration.AssigneeFieldEnvironmentVariable
        };

        var previous = names.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable);

        YouTrackConfiguration configuration;

        try
        {
            Environment.SetEnvironmentVariable(
                YouTrackConfiguration.BaseUrlEnvironmentVariable,
                "https://example.invalid/");
            Environment.SetEnvironmentVariable(
                YouTrackConfiguration.TokenEnvironmentVariable,
                "test-token");
            Environment.SetEnvironmentVariable(
                YouTrackConfiguration.DiscoveryQueryEnvironmentVariable,
                "project: STEPI sort by: {issue id} asc");
            Environment.SetEnvironmentVariable(
                YouTrackConfiguration.SourceIdEnvironmentVariable,
                "youtrack-stepin");
            Environment.SetEnvironmentVariable(
                YouTrackConfiguration.WorkflowStateFieldEnvironmentVariable,
                "Stage");
            Environment.SetEnvironmentVariable(
                YouTrackConfiguration.AssigneeFieldEnvironmentVariable,
                "Assignee");

            configuration = YouTrackConfiguration.FromEnvironment();
        }
        finally
        {
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(
                    name, previous[name]);
            }
        }

        using var handler = new PagedIssueHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = configuration.BaseAddress
        };

        var source = new YouTrackWorkItemSource(
            client, configuration);

        var items = await source.FindReadyAsync();

        Assert.Equal(101, items.Count);
        Assert.Equal(101, items.Select(item =>
            item.ExternalWorkItemId).Distinct().Count());
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains(items, item =>
            item.WorkItemReference == "STEPI-100");
    }

    private sealed class PagedIssueHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            var query = request.RequestUri!.Query;
            var start = query.Contains(
                "$skip=100", StringComparison.Ordinal)
                ? 100
                : 0;

            var count = start == 0 ? 100 : 1;

            var issues = Enumerable.Range(start, count)
                .Select(index => new
                {
                    id = $"3-{index}",
                    idReadable = $"STEPI-{index}",
                    summary = $"Ticket {index}",
                    updated = 1_790_000_000_000L,
                    customFields = new object[]
                    {
                        new
                        {
                            name = "Stage",
                            value = new
                            {
                                id = (string?)null,
                                name = "To Do",
                                login = (string?)null,
                                fullName = (string?)null
                            }
                        },
                        new
                        {
                            name = "Assignee",
                            value = new
                            {
                                id = "2-2",
                                name = "Traily Team Lead",
                                login = "traily-team-lead",
                                fullName = "Traily Team Lead"
                            }
                        }
                    }
                });

            var json = JsonSerializer.Serialize(issues);

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        json, Encoding.UTF8, "application/json")
                });
        }
    }
}