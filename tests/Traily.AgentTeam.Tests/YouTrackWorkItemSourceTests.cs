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
        var configuration = CreateConfiguration();

        using var handler = new PagedIssueHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = configuration.BaseAddress
        };

        var source = new YouTrackWorkItemSource(
            client, configuration);

        var items = await source.FindReadyAsync();

        Assert.Equal(101, items.Count);
        Assert.Equal(
            101,
            items.Select(item => item.ExternalWorkItemId)
                .Distinct()
                .Count());
        Assert.Equal(2, handler.RequestCount);
        Assert.Contains(items, item =>
            item.WorkItemReference == "STEPI-100");
    }

    [Fact]
    public async Task FullReadReturnsCurrentTicketAndRejectsAnotherSource()
    {
        var configuration = CreateConfiguration();

        using var handler = new SingleIssueHandler();
        using var client = new HttpClient(handler)
        {
            BaseAddress = configuration.BaseAddress
        };

        var source = new YouTrackWorkItemSource(
            client, configuration);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.GetRequiredAsync(
                "another-source",
                "3-20"));

        Assert.Equal(0, handler.RequestCount);

        var item = await source.GetRequiredAsync(
            "youtrack-stepin",
            "3-20");

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("3-20", item.ExternalWorkItemId);
        Assert.Equal("STEPI-18", item.Reference);
        Assert.Equal("Current title", item.Title);
        Assert.Equal(
            "Current description",
            item.Description);
        Assert.Equal("To Do", item.State);
        Assert.Equal("2-2", item.ExternalAssigneeId);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(
                1_790_000_000_000L),
            item.SourceUpdatedAt);
    }

    private static YouTrackConfiguration CreateConfiguration()
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

            return YouTrackConfiguration.FromEnvironment();
        }
        finally
        {
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(
                    name,
                    previous[name]);
            }
        }
    }

    private sealed class SingleIssueHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            const string json = """
                {
                  "id": "3-20",
                  "idReadable": "STEPI-18",
                  "summary": "Current title",
                  "description": "Current description",
                  "updated": 1790000000000,
                  "customFields": [
                    {
                      "name": "Stage",
                      "value": { "name": "To Do" }
                    },
                    {
                      "name": "Assignee",
                      "value": { "id": "2-2" }
                    }
                  ]
                }
                """;

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json")
                });
        }
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
                "$skip=100",
                StringComparison.Ordinal)
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
                        json,
                        Encoding.UTF8,
                        "application/json")
                });
        }
    }
}