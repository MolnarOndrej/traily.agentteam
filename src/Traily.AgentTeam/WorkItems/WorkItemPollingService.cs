using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.WorkItems;

public sealed class WorkItemPollingService(
    IServiceScopeFactory scopeFactory,
    WorkItemPollingConfiguration configuration,
    ILogger<WorkItemPollingService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(configuration.Interval);

        try
        {
            do
            {
                try
                {
                    await PollOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Work item poll failed.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task PollOnceAsync(
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        await using var scope =
            scopeFactory.CreateAsyncScope();

        var synchronizer = scope.ServiceProvider
            .GetRequiredService<WorkItemSynchronizer>();

        var results = await synchronizer.SyncAsync(
            cancellationToken);

        var database = scope.ServiceProvider
            .GetRequiredService<TrailyDbContext>();

        var queuedCount = await database.WorkItemJobs
            .CountAsync(
                job => job.Status == WorkItemJobStatus.Queued,
                cancellationToken);

        logger.LogInformation(
            "Work item poll completed in {ElapsedMilliseconds} ms. " +
            "Discovered {DiscoveredCount}; new {NewCount}; " +
            "updated {UpdatedCount}; queued jobs {QueuedCount}.",
            stopwatch.ElapsedMilliseconds,
            results.Count,
            results.Count(result =>
                result.Outcome == WorkItemSyncOutcome.New),
            results.Count(result =>
                result.Outcome == WorkItemSyncOutcome.Updated),
            queuedCount);

        foreach (var result in results.Where(result =>
            result.Outcome is
                WorkItemSyncOutcome.Unmapped or
                WorkItemSyncOutcome.AgentInactive or
                WorkItemSyncOutcome.AssignmentChanged))
        {
            logger.LogWarning(
                "Work item {WorkItemReference}: {Outcome}; " +
                "agent {AgentId}.",
                result.WorkItemReference,
                result.Outcome,
                result.AgentId);
        }
    }
}