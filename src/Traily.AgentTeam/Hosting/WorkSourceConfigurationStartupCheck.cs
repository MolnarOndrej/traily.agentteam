using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Integrations.YouTrack;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.Hosting;

public sealed class WorkSourceConfigurationStartupCheck(
    IServiceScopeFactory scopeFactory, YouTrackConfigurationStore configurations,
    OperationalIssueService issues, ILogger<WorkSourceConfigurationStartupCheck> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrailyDbContext>();
        var sourceIds = await database.ManagedProjects.AsNoTracking()
            .Where(project => project.WorkSourceConnection.ProviderId.ToLower() == "youtrack")
            .OrderBy(project => project.SourceId).Select(project => project.SourceId)
            .ToListAsync(cancellationToken);
        foreach (var sourceId in sourceIds)
        {
            string? diagnostic = null;
            try { _ = await configurations.GetSourceAsync(sourceId, cancellationToken); }
            catch (YouTrackConfigurationException exception) { diagnostic = exception.Message; }
            if (diagnostic is not null)
                logger.LogError("YouTrack configuration for {SourceId} is invalid. {Diagnostic}", sourceId, diagnostic);
            try
            {
                await issues.ObserveAsync(new OperationalObservation(
                    new("Configuration", sourceId, "ConfigureDiscovery"),
                    diagnostic is null ? OperationalAvailability.Available : OperationalAvailability.Unavailable,
                    diagnostic is null ? null : "InvalidConfiguration", diagnostic), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogError("Could not persist source configuration observation. Failure type: {FailureType}.",
                    exception.GetType().Name);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
