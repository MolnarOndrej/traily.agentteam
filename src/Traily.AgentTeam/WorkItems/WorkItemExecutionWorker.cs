using System.Data;
using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.WorkItems;

public sealed class WorkItemExecutionWorker(
    WorkItemClaimService claims,
    TrailyDbContext database,
    IWorkItemReader reader,
    IWorkItemExecutionProvider provider,
    TimeProvider clock)
{
    public async Task<bool> RunNextAsync(
        string workingDirectory,
        string requiredState,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredState);

        var fullDirectory = Path.GetFullPath(workingDirectory);

        if (!Directory.Exists(fullDirectory))
        {
            throw new DirectoryNotFoundException(fullDirectory);
        }

        var claim = await claims.ClaimNextAsync(
            cancellationToken);

        if (claim is null)
        {
            return false;
        }

        var job = await database.WorkItemJobs
            .AsNoTracking()
            .SingleAsync(
                entry => entry.Id == claim.JobId,
                cancellationToken);

        WorkItem ticket;

        try
        {
            ticket = await reader.GetRequiredAsync(
                job.SourceId,
                job.ExternalWorkItemId,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await FinishAsync(
                claim,
                WorkItemJobStatus.Blocked,
                WorkItemStopReason.TicketReadFailed,
                cancellationToken);

            return true;
        }

        if (!string.Equals(
            ticket.ExternalWorkItemId,
            job.ExternalWorkItemId,
            StringComparison.Ordinal))
        {
            await FinishAsync(
                claim,
                WorkItemJobStatus.Blocked,
                WorkItemStopReason.TicketIdentityChanged,
                cancellationToken);

            return true;
        }

        if (!string.Equals(
            ticket.ExternalAssigneeId,
            job.ExternalAssigneeId,
            StringComparison.Ordinal))
        {
            await FinishAsync(
                claim,
                WorkItemJobStatus.Blocked,
                WorkItemStopReason.TicketAssignmentChanged,
                cancellationToken);

            return true;
        }

        if (!string.Equals(
            ticket.State,
            requiredState,
            StringComparison.OrdinalIgnoreCase))
        {
            await FinishAsync(
                claim,
                WorkItemJobStatus.Blocked,
                WorkItemStopReason.TicketStateChanged,
                cancellationToken);

            return true;
        }

        var taskSnapshot = $"""
            # {ticket.Reference} — {ticket.Title}

            {ticket.Description}
            """;

        await PrepareAsync(
            claim,
            ticket,
            taskSnapshot,
            fullDirectory,
            cancellationToken);

        WorkItemExecutionResult result;

        try
        {
            result = await provider.RunAsync(
                new WorkItemExecutionRequest(
                    claim.AgentId,
                    ticket.Reference,
                    taskSnapshot,
                    fullDirectory),
                (sessionId, token) =>
                    SaveSessionAsync(claim, sessionId, token),
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Preserve Running for explicit recovery after shutdown.
            throw;
        }
        catch (Exception)
        {
            await FinishAsync(
                claim,
                WorkItemJobStatus.Blocked,
                WorkItemStopReason.ProviderFailed,
                cancellationToken);

            return true;
        }

        // Exactly one of completion and stop reason must be present.
        if (result.Completed == (result.StopReason is not null))
        {
            await FinishAsync(
                claim,
                WorkItemJobStatus.Blocked,
                WorkItemStopReason.ProviderFailed,
                cancellationToken);

            return true;
        }

        await FinishAsync(
            claim,
            result.Completed
                ? WorkItemJobStatus.AwaitingReview
                : WorkItemJobStatus.Blocked,
            result.StopReason,
            cancellationToken);

        return true;
    }

    private async Task PrepareAsync(
        WorkItemClaim claim,
        WorkItem ticket,
        string taskSnapshot,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        var updated = await database.CurrentAttempt(claim)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        entry => entry.TaskSnapshot,
                        taskSnapshot)
                    .SetProperty(
                        entry => entry.TaskSourceUpdatedAt,
                        ticket.SourceUpdatedAt)
                    .SetProperty(
                        entry => entry.WorkingDirectory,
                        workingDirectory)
                    .SetProperty(
                        entry => entry.ProviderId,
                        provider.ProviderId)
                    .SetProperty(
                        entry => entry.StartedAt,
                        now),
                cancellationToken);

        if (updated != 1)
        {
            throw new InvalidOperationException(
                "The claimed attempt is no longer current.");
        }
    }

    private async Task SaveSessionAsync(
        WorkItemClaim claim,
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var updated = await database.CurrentAttempt(claim)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    entry => entry.ProviderSessionId,
                    sessionId),
                cancellationToken);

        if (updated != 1)
        {
            throw new InvalidOperationException(
                "The claimed attempt is no longer current.");
        }
    }

    private async Task FinishAsync(
        WorkItemClaim claim,
        WorkItemJobStatus status,
        WorkItemStopReason? reason,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var jobsUpdated = await database.CurrentJob(claim)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, status)
                    .SetProperty(job => job.UpdatedAt, now),
                cancellationToken);

        if (jobsUpdated != 1)
        {
            throw new InvalidOperationException(
                "The claimed job is no longer current.");
        }

        var attemptsUpdated = await database
            .WorkItemExecutionAttempts
            .Where(entry =>
                entry.Id == claim.AttemptId &&
                entry.WorkItemJobId == claim.JobId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        entry => entry.StopReason,
                        reason)
                    .SetProperty(
                        entry => entry.FinishedAt,
                        status == WorkItemJobStatus.AwaitingReview
                            ? now
                            : (DateTimeOffset?)null),
                cancellationToken);

        if (attemptsUpdated != 1)
        {
            throw new InvalidOperationException(
                "The claimed attempt was not found.");
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
