using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.WorkSources;

public sealed class AccessCheckedWorkItemDiscovery(
    IWorkItemDiscovery inner,
    WorkSourceAccessService accessService) : IWorkItemDiscovery
{
    public async Task<IReadOnlyList<DiscoveredWorkItem>> FindReadyAsync(
        CancellationToken cancellationToken = default)
    {
        await accessService.CheckAllAsync(cancellationToken);
        return await inner.FindReadyAsync(cancellationToken);
    }
}
