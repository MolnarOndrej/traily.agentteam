namespace Traily.AgentTeam.WorkItems;

public sealed record DiscoveredWorkItem(
    string Id,
    string Title,
    string State,
    string AssigneeId,
    DateTimeOffset UpdatedAt);

public interface IWorkItemDiscovery
{
    Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(
        CancellationToken cancellationToken = default);
}