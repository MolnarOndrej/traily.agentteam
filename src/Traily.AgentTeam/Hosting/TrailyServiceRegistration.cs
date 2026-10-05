using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Integrations.YouTrack;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.Orchestration;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.Runtime;
using Traily.AgentTeam.WorkItems;
using Traily.AgentTeam.WorkSources;

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

        services.AddSingleton<ICodexProcessClient, CodexProcessClient>();
        services.AddScoped<IWorkItemExecutionProvider, CodexWorkItemExecutionProvider>();
        services.AddScoped<WorkItemExecutionWorker>();
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

        services.AddSingleton<OperationalIssueService>();
        services.AddKeyedSingleton<HttpClient>("work-source-access", (_, _) =>
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(20)
            });
        services.AddSingleton<IWorkSourceAccessCheck>(provider =>
            new YouTrackWorkSourceAccessCheck(
                provider.GetRequiredKeyedService<HttpClient>("work-source-access"),
                () => provider.GetRequiredService<YouTrackConfiguration>()));
        services.AddScoped<WorkSourceAccessService>();
        services.AddScoped<IWorkItemDiscovery>(
            provider =>
            {
                var configuration = provider
                    .GetRequiredService<YouTrackConfiguration>();

                var discovery = new ObservedYouTrackDiscovery(
                    provider.GetRequiredService<YouTrackWorkItemSource>(),
                    configuration.SourceId,
                    provider.GetRequiredService<OperationalIssueService>(),
                    provider.GetRequiredService<
                        ILogger<ObservedYouTrackDiscovery>>());
                return new AccessCheckedWorkItemDiscovery(discovery,
                    provider.GetRequiredService<WorkSourceAccessService>());
            });

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
        // Report retained issues, check configured access, then validate pilot discovery.
        services.AddHostedService<OperationalIssueStartupReporter>();
        services.AddHostedService<WorkSourceAccessStartupCheck>();
        services.AddSingleton<YouTrackConfigurationStartupCheck>();
        services.AddHostedService<YouTrackConfigurationStartupCheck>(
            provider => provider.GetRequiredService<YouTrackConfigurationStartupCheck>());
        services.AddHostedService<WorkItemPollingService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<WorkItemClaimService>();
        services.AddScoped<RepositoryAccessService>();
        services.AddScoped<GitCheckoutPreflightService>();
        services.AddScoped<WorkItemPlanningService>();

        return services;
    }
}
