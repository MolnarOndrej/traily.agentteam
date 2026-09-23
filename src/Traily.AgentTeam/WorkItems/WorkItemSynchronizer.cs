using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.WorkItems;

public sealed class WorkItemSynchronizer(
    IWorkItemDiscovery discovery,
    TrailyDbContext database)
{
    public async Task<IReadOnlyList<WorkItemSyncResult>> SyncAsync(
        CancellationToken cancellationToken = default)
    {
        var discoveredItems =
            await discovery.FindReadyAsync(cancellationToken);

        var results =
            new List<WorkItemSyncResult>(discoveredItems.Count);

        foreach (var item in discoveredItems)
        {
            var identity = await database.AgentExternalIdentities
                .AsNoTracking()
                .Include(entry => entry.Agent)
                .SingleOrDefaultAsync(
                    entry =>
                        entry.SourceId == item.SourceId &&
                        entry.ExternalUserId == item.ExternalAssigneeId,
                    cancellationToken);

            if (identity is null)
            {
                results.Add(new(
                    item.WorkItemReference,
                    WorkItemSyncOutcome.Unmapped,
                    null,
                    null));
                continue;
            }

            if (!identity.Agent.IsEnabled ||
                identity.Agent.DeletionRequestedAt is not null)
            {
                results.Add(new(
                    item.WorkItemReference,
                    WorkItemSyncOutcome.AgentInactive,
                    identity.AgentId,
                    null));
                continue;
            }

            var job = await database.WorkItemJobs
                .SingleOrDefaultAsync(
                    existing =>
                        existing.SourceId == item.SourceId &&
                        existing.ExternalWorkItemId ==
                            item.ExternalWorkItemId,
                    cancellationToken);

            if (job is null)
            {
                var now = DateTimeOffset.UtcNow;

                database.WorkItemJobs.Add(new WorkItemJob
                {
                    Id = Guid.NewGuid(),
                    SourceId = item.SourceId,
                    ExternalWorkItemId = item.ExternalWorkItemId,
                    WorkItemReference = item.WorkItemReference,
                    Title = item.Title,
                    ExternalAssigneeId = item.ExternalAssigneeId,
                    AgentId = identity.AgentId,
                    Status = WorkItemJobStatus.Queued,
                    SourceUpdatedAt = item.SourceUpdatedAt,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                results.Add(new(
                    item.WorkItemReference,
                    WorkItemSyncOutcome.New,
                    identity.AgentId,
                    WorkItemJobStatus.Queued));
                continue;
            }

            if (job.AgentId != identity.AgentId ||
                job.ExternalAssigneeId != item.ExternalAssigneeId)
            {
                results.Add(new(
                    item.WorkItemReference,
                    WorkItemSyncOutcome.AssignmentChanged,
                    job.AgentId,
                    job.Status));
                continue;
            }

            if (item.SourceUpdatedAt > job.SourceUpdatedAt)
            {
                job.WorkItemReference = item.WorkItemReference;
                job.Title = item.Title;
                job.SourceUpdatedAt = item.SourceUpdatedAt;
                job.UpdatedAt = DateTimeOffset.UtcNow;

                results.Add(new(
                    item.WorkItemReference,
                    WorkItemSyncOutcome.Updated,
                    job.AgentId,
                    job.Status));
                continue;
            }

            results.Add(new(
                item.WorkItemReference,
                job.Status == WorkItemJobStatus.Queued
                    ? WorkItemSyncOutcome.AlreadyQueued
                    : WorkItemSyncOutcome.Unchanged,
                job.AgentId,
                job.Status));
        }

        await database.SaveChangesAsync(cancellationToken);
        return results;
    }
}