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
var youTrackConfiguration = YouTrackConfiguration.FromEnvironment();
var databasePath = DatabaseStorageConfiguration.ResolvePath();

DatabaseStorageConfiguration.EnsureDirectoryExists(databasePath);

var services = new ServiceCollection();

services.AddSingleton<IAgentCatalog, AgentCatalog>();
services.AddSingleton(
    _ => new AgentInstructionsLoader(rootDirectory));
services.AddSingleton<CodexProcessClient>();
services.AddSingleton<CodexResponseParser>();
services.AddSingleton<IAgentRunner, CodexAgentRunner>();
services.AddSingleton<IExecutionTraceWriter>(
    _ => new FileExecutionTraceWriter(traceDirectory));
services.AddSingleton<AgentTaskInvoker>();
services.AddSingleton(youTrackConfiguration);
services.AddSingleton(
    _ => new HttpClient
    {
        BaseAddress = youTrackConfiguration.BaseAddress
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


using var serviceProvider = services.BuildServiceProvider();

if (args.Any(argument =>
    string.Equals(
        argument,
        "--discover",
        StringComparison.OrdinalIgnoreCase)))
{
    var workItemDiscovery =
        serviceProvider.GetRequiredService<IWorkItemDiscovery>();

    var discoveredWorkItems =
        await workItemDiscovery.FindReadyAsync();

    Console.WriteLine(
        $"Discovered {discoveredWorkItems.Count} " +
        "ready work item(s).");

    foreach (var ticketItem in discoveredWorkItems)
    {
        Console.WriteLine(
            $"{ticketItem.Id} | " +
            $"{ticketItem.State} | " +
            $"{ticketItem.AssigneeId} | " +
            $"{ticketItem.Title} | " +
            $"{ticketItem.UpdatedAt:O}");
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