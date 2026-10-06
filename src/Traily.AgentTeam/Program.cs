using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Configuration;
using Traily.AgentTeam.Hosting;
using Traily.AgentTeam.WorkItems;
using Traily.AgentTeam.WorkSources;
using Traily.AgentTeam.Operations;
var builder = Host.CreateApplicationBuilder();
builder.Services.AddTrailyServices();

using var host = builder.Build();
var serviceProvider = host.Services;

if (args.Any(argument => string.Equals(argument, "--set-youtrack-token", StringComparison.OrdinalIgnoreCase)))
{
    if (args.Length != 2 || !string.Equals(args[0], "--set-youtrack-token", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(args[1]))
    {
        Console.Error.WriteLine("Usage: --set-youtrack-token <connection-id>. The token is entered at a hidden prompt.");
        Environment.ExitCode = 1;
        return;
    }
    Environment.ExitCode = await serviceProvider.GetRequiredService<YouTrackTokenConfigurationCommand>()
        .RunAsync(args[1]);
    return;
}

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
    string.Equals(argument, "--check-access", StringComparison.OrdinalIgnoreCase)))
{
    await using var scope = serviceProvider.CreateAsyncScope();
    var observations = await scope.ServiceProvider.GetRequiredService<WorkSourceAccessService>()
        .CheckAllAsync();
    foreach (var observation in observations)
    {
        Console.WriteLine($"{observation.Scope.Type} {observation.Scope.Id} | " +
            $"{observation.Scope.Capability} | {observation.Availability} | " +
            $"{observation.ReasonCode ?? "-"}");
    }
    if (!observations.Any(observation => observation.Scope.Type is "WorkSource" or "WorkSourceConnection"))
        Console.WriteLine("No work-source access targets were checked.");
    Environment.ExitCode = observations.Any(observation =>
        observation.Availability != OperationalAvailability.Available) ||
        !observations.Any(observation => observation.Scope.Type == "WorkSource") ? 1 : 0;
    return;
}

if (args.Any(argument =>
    string.Equals(
        argument,
        "--sync",
        StringComparison.OrdinalIgnoreCase)))
{
    try
    {
        await serviceProvider.GetRequiredService<WorkSourceConfigurationStartupCheck>()
            .StartAsync(CancellationToken.None);
    }
    catch (YouTrackConfigurationException)
    {
        Environment.ExitCode = 1;
        return;
    }

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

if (args.Any(argument =>
    string.Equals(
        argument,
        "--serve",
        StringComparison.OrdinalIgnoreCase)))
{
    try
    {
        await host.RunAsync();
    }
    catch (YouTrackConfigurationException)
    {
        Environment.ExitCode = 1;
    }
    return;
}
