using Microsoft.Extensions.DependencyInjection;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Runtime;
using Traily.AgentTeam.Orchestration;

var rootDirectory = Directory.GetCurrentDirectory();
var traceDirectory = TraceStorageConfiguration.ResolveDirectory();

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

using var serviceProvider = services.BuildServiceProvider();

var agentTaskInvoker =
    serviceProvider.GetRequiredService<AgentTaskInvoker>();

const string agentId = "team-lead";
const string taskId = "STEPI-18";
var taskPath = Path.Combine(rootDirectory, "tasks", $"{taskId}.md");
var taskDescription = await File.ReadAllTextAsync(taskPath);

Console.WriteLine($"Running Team Lead analysis of {taskId}...");

var result = await agentTaskInvoker.InvokeAsync(
    new AgentTaskInvocation(
        AgentId: agentId,
        TaskId: taskId,
        TaskDescription: taskDescription,
        WorkingDirectory: rootDirectory));

if (!result.Success)
{
    throw new InvalidOperationException(
        $"Agent execution failed: {result.Output}");
}

Console.WriteLine();
Console.WriteLine(result.Output);