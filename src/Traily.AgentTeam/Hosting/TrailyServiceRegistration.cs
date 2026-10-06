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

        CredentialProtectionConfiguration.Register(services);
        services.AddSingleton(provider => new Lazy<AccessTokenProtector>(
            () => provider.GetRequiredService<AccessTokenProtector>()));
        services.AddSingleton<YouTrackConfigurationStore>();
        services.AddSingleton<YouTrackTokenConfigurationCommand>();
        services.AddSingleton<HttpClient>(_ =>
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(20)
            });
        services.AddSingleton<DatabaseYouTrackWorkItemSource>();
        services.AddSingleton<IWorkItemReader>(provider =>
            provider.GetRequiredService<DatabaseYouTrackWorkItemSource>());

        services.AddSingleton<OperationalIssueService>();
        services.AddKeyedSingleton<HttpClient>("work-source-access", (_, _) =>
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = TimeSpan.FromSeconds(20)
            });
        services.AddSingleton<IWorkSourceAccessCheck>(provider =>
            new YouTrackWorkSourceAccessCheck(
                provider.GetRequiredKeyedService<HttpClient>("work-source-access"),
                provider.GetRequiredService<YouTrackConfigurationStore>()));
        services.AddScoped<WorkSourceAccessService>();
        services.AddScoped<IWorkItemDiscovery>(
            provider =>
                new AccessCheckedWorkItemDiscovery(
                    provider.GetRequiredService<DatabaseYouTrackWorkItemSource>(),
                    provider.GetRequiredService<WorkSourceAccessService>()));

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
        // Retained history is visible before connection checks and per-source validation.
        services.AddHostedService<OperationalIssueStartupReporter>();
        services.AddHostedService<WorkSourceAccessStartupCheck>();
        services.AddSingleton<WorkSourceConfigurationStartupCheck>();
        services.AddHostedService<WorkSourceConfigurationStartupCheck>(
            provider => provider.GetRequiredService<WorkSourceConfigurationStartupCheck>());
        services.AddHostedService<WorkItemPollingService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<WorkItemClaimService>();
        services.AddScoped<RepositoryAccessService>();
        services.AddScoped<GitCheckoutPreflightService>();
        services.AddScoped<WorkItemPlanningService>();

        return services;
    }
}
