using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Traily.AgentTeam.WorkSources;

namespace Traily.AgentTeam.Hosting;

public sealed class WorkSourceAccessStartupCheck(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<WorkSourceAccessService>()
            .CheckAllAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
