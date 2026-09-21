using Microsoft.Extensions.DependencyInjection;
using Traily.AgentTeam.Agents;

var rootDirectory = Directory.GetCurrentDirectory();

var services = new ServiceCollection();

services.AddSingleton<IAgentCatalog, AgentCatalog>();

services.AddSingleton(
    _ => new AgentInstructionsLoader(rootDirectory));

using var serviceProvider = services.BuildServiceProvider();

var agentCatalog = serviceProvider.GetRequiredService<IAgentCatalog>();

var agentInstructionsLoader =
    serviceProvider.GetRequiredService<AgentInstructionsLoader>();

var teamLead = agentCatalog.GetRequired("team-lead");

var instructions = await agentInstructionsLoader.LoadAsync(teamLead);

var roleInstructionsPath = Path.Combine(
    rootDirectory,
    teamLead.InstructionsPath);

var skillInstructionsPath = Path.Combine(
    rootDirectory,
    AgentFileConventions.SkillsDirectory,
    "task-analysis",
    AgentFileConventions.SkillInstructionsFileName);

var roleInstructions = await File.ReadAllTextAsync(
    roleInstructionsPath);

var skillInstructions = await File.ReadAllTextAsync(
    skillInstructionsPath);

if (!instructions.Contains(roleInstructions, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Team Lead role instructions were not loaded correctly.");
}

if (!instructions.Contains(skillInstructions, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Task-analysis skill instructions were not loaded correctly.");
}

Console.WriteLine($"Agent: {teamLead.Role}");
Console.WriteLine($"Agent ID: {teamLead.Id}");
Console.WriteLine($"Assigned skills: {string.Join(", ", teamLead.SkillIds)}");
Console.WriteLine($"Loaded instructions: {instructions.Length} characters");
Console.WriteLine("Role instructions: verified");
Console.WriteLine("Task-analysis skill: verified");