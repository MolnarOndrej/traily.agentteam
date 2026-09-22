namespace Traily.AgentTeam.WorkItems;

public sealed record WorkItem(
    string Id,
    string Title,
    string Description);

public interface IWorkItemReader
{
    Task<WorkItem> GetRequiredAsync(
        string workItemId,
        CancellationToken cancellationToken = default);
}