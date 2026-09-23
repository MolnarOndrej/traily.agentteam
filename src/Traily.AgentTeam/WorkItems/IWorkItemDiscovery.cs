namespace Traily.AgentTeam.WorkItems;

public sealed record DiscoveredWorkItem(
    string SourceId,
    string ExternalWorkItemId,
    string WorkItemReference,
    string Title,
    string State,
    string ExternalAssigneeId,
    string AssigneeLogin,
    string? AssigneeDisplayName,
    DateTimeOffset SourceUpdatedAt);

public interface IWorkItemDiscovery
{
    Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(
        CancellationToken cancellationToken = default);
}