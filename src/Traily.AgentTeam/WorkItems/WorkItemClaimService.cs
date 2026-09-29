using System.Data;
using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.WorkItems;

public sealed record WorkItemClaim(
    Guid JobId,
    Guid AttemptId,
    string AgentId);

public sealed class WorkItemClaimService(
    TrailyDbContext database,
    TimeProvider clock)
{
    public async Task<WorkItemClaim?> ClaimNextAsync(
        CancellationToken cancellationToken = default)
    {
        // Keep the capacity check, job update, and attempt insert
        // in the same short transaction.
        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var candidate = await database.WorkItemJobs
            .AsNoTracking()
            .Where(job =>
                job.Status == WorkItemJobStatus.Queued &&
                job.Agent.IsEnabled &&
                job.Agent.DeletionRequestedAt == null &&
                database.WorkItemJobs.Count(active =>
                    active.AgentId == job.AgentId &&
                    active.Status == WorkItemJobStatus.Running)
                    < job.Agent.MaxConcurrentJobs)
            .OrderBy(job => job.SourceId)
            .ThenBy(job => job.ExternalWorkItemId)
            .Select(job => new
            {
                job.Id,
                job.AgentId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
        {
            return null;
        }

        var attemptId = Guid.NewGuid();
        var now = clock.GetUtcNow();

        // The status condition also prevents claiming this particular
        // job if another operation changed it.
        var updated = await database.WorkItemJobs
            .Where(job =>
                job.Id == candidate.Id &&
                job.Status == WorkItemJobStatus.Queued)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        job => job.Status,
                        WorkItemJobStatus.Running)
                    .SetProperty(
                        job => job.CurrentAttemptId,
                        (Guid?)attemptId)
                    .SetProperty(
                        job => job.UpdatedAt,
                        now),
                cancellationToken);

        if (updated != 1)
        {
            // Disposing the uncommitted transaction rolls it back.
            return null;
        }

        database.WorkItemExecutionAttempts.Add(
            new WorkItemExecutionAttempt
            {
                Id = attemptId,
                WorkItemJobId = candidate.Id,
                CreatedAt = now
            });

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new WorkItemClaim(
            candidate.Id,
            attemptId,
            candidate.AgentId);
    }
}