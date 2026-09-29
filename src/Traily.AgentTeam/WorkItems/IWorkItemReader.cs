namespace Traily.AgentTeam.WorkItems;

public sealed record WorkItem(
    string ExternalWorkItemId,
    string Reference,
    string Title,
    string Description,
    string State,
    string ExternalAssigneeId,
    DateTimeOffset SourceUpdatedAt);

public interface IWorkItemReader
{
    Task<WorkItem> GetRequiredAsync(
        string sourceId,
        string externalWorkItemId,
        CancellationToken cancellationToken = default);
}