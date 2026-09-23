using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Integrations.YouTrack;
using Traily.AgentTeam.Orchestration;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.Runtime;
using Traily.AgentTeam.WorkItems;

var rootDirectory = Directory.GetCurrentDirectory();
var traceDirectory = TraceStorageConfiguration.ResolveDirectory();
var databasePath = DatabaseStorageConfiguration.ResolvePath();

DatabaseStorageConfiguration.EnsureDirectoryExists(databasePath);

var services = new ServiceCollection();

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
        var configuration =
            provider.GetRequiredService<YouTrackConfiguration>();

        return new HttpClient
        {
            BaseAddress = configuration.BaseAddress
        };
    });
services.AddSingleton<YouTrackWorkItemSource>();
services.AddSingleton<IWorkItemReader>(
    provider =>
        provider.GetRequiredService<
            YouTrackWorkItemSource>());

services.AddSingleton<IWorkItemDiscovery>(
    provider =>
        provider.GetRequiredService<
            YouTrackWorkItemSource>());

services.AddDbContext<TrailyDbContext>(
    options => options.UseSqlite(
        DatabaseStorageConfiguration.CreateConnectionString(
            databasePath)));

services.AddScoped<IAgentCatalog, DatabaseAgentCatalog>();
services.AddSingleton<AgentInstructionsComposer>();
services.AddScoped<AgentTaskInvoker>();
services.AddScoped<WorkItemSynchronizer>();

using var serviceProvider = services.BuildServiceProvider();

if (args.Any(argument =>
    string.Equals(
        argument,
        "--list-agents",
        StringComparison.OrdinalIgnoreCase)))
{
    await using var scope =
        serviceProvider.CreateAsyncScope();

    var catalog =
        scope.ServiceProvider
            .GetRequiredService<IAgentCatalog>();

    var composer =
        scope.ServiceProvider
            .GetRequiredService<AgentInstructionsComposer>();

    var agents = await catalog.ListAsync();

    Console.WriteLine(
        $"Found {agents.Count} agent profile(s).");

    foreach (var agent in agents)
    {
        if (!agent.IsEnabled)
        {
            Console.WriteLine(
                $"{agent.Id} | disabled | {agent.Name} | " +
                $"{agent.SkillCount} skill(s) | " +
                $"max concurrency {agent.MaxConcurrentJobs}");

            continue;
        }

        var definition = await catalog.GetRequiredAsync(agent.Id);
        var instructions = composer.Compose(definition);

        Console.WriteLine(
            $"{agent.Id} | enabled | {agent.Name} | " +
            $"{definition.Skills.Count} skill(s) | " +
            $"max concurrency {definition.MaxConcurrentJobs} | " +
            $"{instructions.Length} composed instruction characters");
    }

    return;
}

if (args.Any(argument =>
    string.Equals(
        argument,
        "--sync",
        StringComparison.OrdinalIgnoreCase)))
{
    await using var scope =
        serviceProvider.CreateAsyncScope();

    var synchronizer = scope.ServiceProvider
        .GetRequiredService<WorkItemSynchronizer>();

    var results = await synchronizer.SyncAsync();

    Console.WriteLine(
        $"Processed {results.Count} discovered work item(s).");

    foreach (var result in results)
    {
        Console.WriteLine(
            $"{result.WorkItemReference} | {result.Outcome} | " +
            $"agent {result.AgentId ?? "-"} | " +
            $"status {result.Status?.ToString() ?? "-"}");
    }

    return;
}

// var agentTaskInvoker =
//     serviceProvider.GetRequiredService<AgentTaskInvoker>();

// var workItemReader =
//     serviceProvider.GetRequiredService<IWorkItemReader>();

// const string agentId = "team-lead";
// const string taskId = "STEPI-18";

// var workItem =
//     await workItemReader.GetRequiredAsync(taskId);

// var taskDescription = $"""
//     # {workItem.Id} — {workItem.Title}

//     {workItem.Description}
//     """;

// Console.WriteLine($"Running Team Lead analysis of {taskId}...");

// var result = await agentTaskInvoker.InvokeAsync(
//     new AgentTaskInvocation(
//         AgentId: agentId,
//         TaskId: taskId,
//         TaskDescription: taskDescription,
//         WorkingDirectory: rootDirectory));

// if (!result.Success)
// {
//     throw new InvalidOperationException(
//         $"Agent execution failed: {result.Output}");
// }

// Console.WriteLine();
// Console.WriteLine(result.Output);