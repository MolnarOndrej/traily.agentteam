using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.Hosting;

public sealed class OperationalIssueStartupReporter(
    IServiceScopeFactory scopeFactory,
    ILogger<OperationalIssueStartupReporter> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var database = scope.ServiceProvider
                .GetRequiredService<TrailyDbContext>();
            var issues = await GetUnresolvedIssuesAsync(
                database,
                cancellationToken);

            logger.LogInformation(
                "Startup found {UnresolvedIssueCount} stored unresolved operational issue(s). " +
                "Stored observations have not been revalidated during this startup report.",
                issues.Count);

            foreach (var issue in issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogWarning(
                    "Stored unresolved operational issue {IssueId}: " +
                    "{ScopeType} {ScopeId}; capability {Capability}; " +
                    "{Availability}; {ReasonCode}; {Message}; " +
                    "last observed {LastObservedAt}. Awaiting revalidation.",
                    issue.Id,
                    issue.ScopeType,
                    issue.ScopeId,
                    issue.Capability,
                    issue.Availability,
                    issue.ReasonCode,
                    issue.Message,
                    issue.LastObservedAt);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Do not expose raw database diagnostics or interpret a failed read as recovery.
            logger.LogError(
                "Could not read stored unresolved operational issues at startup. " +
                "Their status is unknown; discovery polling will continue if startup configuration is valid. " +
                "Failure type: {FailureType}.",
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<IReadOnlyList<OperationalIssue>> GetUnresolvedIssuesAsync(
        TrailyDbContext database,
        CancellationToken cancellationToken)
    {
        return await database.OperationalIssues
            .AsNoTracking()
            .Where(issue => issue.ResolvedAt == null)
            .OrderBy(issue => issue.ScopeType)
            .ThenBy(issue => issue.ScopeId)
            .ThenBy(issue => issue.Capability)
            .ToListAsync(cancellationToken);
    }
}
