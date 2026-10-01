using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Operations;

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
            var query = scope.ServiceProvider
                .GetRequiredService<OperationalIssueQuery>();
            var issues = await query.GetUnresolvedAsync(cancellationToken);

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
                "Their status is unknown; discovery polling will continue. " +
                "Failure type: {FailureType}.",
                exception.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
