using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Integrations.YouTrack;
using Traily.AgentTeam.Orchestration;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.Runtime;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Hosting;

public static class TrailyServiceRegistration
{
    public static IServiceCollection AddTrailyServices(
        this IServiceCollection services)
    {
        var traceDirectory =
            TraceStorageConfiguration.ResolveDirectory();
        var databasePath =
            DatabaseStorageConfiguration.ResolvePath();

        DatabaseStorageConfiguration.EnsureDirectoryExists(
            databasePath);

        services.AddSingleton<CodexProcessClient>();
        services.AddSingleton<CodexResponseParser>();
        services.AddSingleton<IAgentRunner, CodexAgentRunner>();
        services.AddSingleton<IExecutionTraceWriter>(
            _ => new FileExecutionTraceWriter(traceDirectory));

        services.AddSingleton(
            _ => YouTrackConfiguration.FromEnvironment());

        services.AddSingleton(
            provider =>
            {
                var configuration = provider
                    .GetRequiredService<YouTrackConfiguration>();

                return new HttpClient
                {
                    BaseAddress = configuration.BaseAddress
                };
            });

        services.AddSingleton<YouTrackWorkItemSource>();

        services.AddSingleton<IWorkItemReader>(
            provider => provider
                .GetRequiredService<YouTrackWorkItemSource>());

        services.AddSingleton<IWorkItemDiscovery>(
            provider => provider
                .GetRequiredService<YouTrackWorkItemSource>());

        services.AddDbContext<TrailyDbContext>(
            options => options.UseSqlite(
                DatabaseStorageConfiguration
                    .CreateConnectionString(databasePath)));

        services.AddScoped<IAgentCatalog, DatabaseAgentCatalog>();
        services.AddSingleton<AgentInstructionsComposer>();
        services.AddScoped<AgentTaskInvoker>();
        services.AddScoped<WorkItemSynchronizer>();

        services.AddSingleton(
            _ => WorkItemPollingConfiguration.FromEnvironment());
        services.AddHostedService<WorkItemPollingService>();

        return services;
    }
}