using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.WorkItems;

public sealed class WorkItemPlanningService(
    TrailyDbContext database,
    RepositoryAccessService repositoryAccess,
    TimeProvider clock)
{
    public async Task BeginAsync(
        WorkItemClaim claim,
        WorkItemPlanningInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.TaskSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.EffectivePrompt);
        ArgumentNullException.ThrowIfNull(input.Repositories);

        if (input.Repositories.Count == 0 ||
            input.Repositories.Any(repository =>
                string.IsNullOrWhiteSpace(repository.RepositoryId) ||
                string.IsNullOrWhiteSpace(repository.CommitId)) ||
            input.Repositories.Select(repository => repository.RepositoryId)
                .Distinct(StringComparer.Ordinal).Count()
                != input.Repositories.Count)
        {
            throw new ArgumentException(
                "Planning requires distinct repository IDs and inspected commits.",
                nameof(input));
        }

        // Persist and validate the same snapshot of the caller's collection.
        var capturedInput = input with
        {
            Repositories = input.Repositories.ToArray()
        };
        var inputJson = JsonSerializer.Serialize(capturedInput);

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var job = await GetCurrentJobAsync(claim, cancellationToken);

        var accessible = await repositoryAccess
            .GetAccessibleRepositoriesAsync(
                job.SourceId,
                claim.AgentId,
                cancellationToken);

        var allowedIds = accessible
            .Select(repository => repository.RepositoryId)
            .ToHashSet(StringComparer.Ordinal);

        if (capturedInput.Repositories.Any(repository =>
                !allowedIds.Contains(repository.RepositoryId)))
        {
            throw new InvalidOperationException(
                "Planning input includes a repository without Read access.");
        }

        var updated = await database.CurrentAttempt(claim)
            .Where(attempt =>
                attempt.Phase == null &&
                attempt.StartedAt == null &&
                attempt.StopReason == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        attempt => attempt.Phase,
                        (WorkItemExecutionPhase?)
                            WorkItemExecutionPhase.Planning)
                    .SetProperty(
                        attempt => attempt.TaskSnapshot,
                        capturedInput.TaskSnapshot)
                    .SetProperty(
                        attempt => attempt.TaskSourceUpdatedAt,
                        (DateTimeOffset?)capturedInput.TaskSourceUpdatedAt)
                    .SetProperty(
                        attempt => attempt.PlanningInputJson,
                        inputJson),
                cancellationToken);

        RequireOne(updated);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveResultAsync(
        WorkItemClaim claim,
        WorkItemPlanningResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        // Persist and validate the same snapshot, including malformed results.
        var capturedResult = result with
        {
            SelectedRepositories = result.SelectedRepositories?.ToArray()!
        };
        var resultJson = JsonSerializer.Serialize(capturedResult);

        await using var transaction =
            await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var job = await GetCurrentJobAsync(claim, cancellationToken);

        var attempt = await database.CurrentAttempt(claim)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (attempt is null ||
            attempt.Phase != WorkItemExecutionPhase.Planning ||
            attempt.PlanningInputJson is null ||
            attempt.PlanningResultJson is not null)
        {
            throw new InvalidOperationException(
                "The attempt is not awaiting a planning result.");
        }

        var input = JsonSerializer.Deserialize<WorkItemPlanningInput>(
            attempt.PlanningInputJson)
            ?? throw new InvalidOperationException(
                "The saved planning input is invalid.");

        var reason = WorkItemPlanningResultValidator.GetStopReason(capturedResult, input);

        if (reason is null)
        {
            // Permissions may have changed during planning.
            var accessible = await repositoryAccess
                .GetAccessibleRepositoriesAsync(
                    job.SourceId,
                    claim.AgentId,
                    cancellationToken);

            var allowedIds = accessible
                .Select(repository => repository.RepositoryId)
                .ToHashSet(StringComparer.Ordinal);

            if (capturedResult.SelectedRepositories.Any(repository =>
                    !allowedIds.Contains(repository.RepositoryId)))
            {
                reason = WorkItemStopReason.RepositoryAccessDenied;
            }
        }

        var now = clock.GetUtcNow();

        var jobsUpdated = await database.CurrentJob(claim)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        entry => entry.Status,
                        reason == null
                            ? WorkItemJobStatus.Running
                            : WorkItemJobStatus.Blocked)
                    .SetProperty(entry => entry.UpdatedAt, now),
                cancellationToken);

        RequireOne(jobsUpdated);

        // The job fence was checked above in this same transaction.
        var attemptsUpdated = await database.WorkItemExecutionAttempts
            .Where(entry =>
                entry.Id == claim.AttemptId &&
                entry.WorkItemJobId == claim.JobId &&
                entry.Phase == WorkItemExecutionPhase.Planning &&
                entry.PlanningResultJson == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        entry => entry.PlanningResultJson,
                        resultJson)
                    .SetProperty(
                        entry => entry.PlanningCompletedAt,
                        (DateTimeOffset?)now)
                    .SetProperty(
                        entry => entry.Phase,
                        WorkItemExecutionPhase.PlanningComplete)
                    .SetProperty(entry => entry.StopReason, reason),
                cancellationToken);

        RequireOne(attemptsUpdated);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<WorkItemJob> GetCurrentJobAsync(
        WorkItemClaim claim,
        CancellationToken cancellationToken)
    {
        return await database.CurrentJob(claim)
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "The claimed job is no longer current.");
    }

    private static void RequireOne(int updated)
    {
        if (updated != 1)
        {
            throw new InvalidOperationException(
                "The planning transition is no longer valid.");
        }
    }
}
