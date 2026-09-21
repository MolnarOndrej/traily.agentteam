using Microsoft.Extensions.DependencyInjection;
using Traily.AgentTeam.Runtime;
using Traily.AgentTeam.Agents;

var rootDirectory = Directory.GetCurrentDirectory();

var services = new ServiceCollection();

services.AddSingleton<IAgentCatalog, AgentCatalog>();
services.AddSingleton(
    _ => new AgentInstructionsLoader(rootDirectory));
services.AddSingleton<CodexProcessClient>();
services.AddSingleton<CodexResponseParser>();
services.AddSingleton<IAgentRunner, CodexAgentRunner>();

using var serviceProvider = services.BuildServiceProvider();

var agentCatalog = serviceProvider.GetRequiredService<IAgentCatalog>();

var agentInstructionsLoader =
    serviceProvider.GetRequiredService<AgentInstructionsLoader>();

var teamLead = agentCatalog.GetRequired("team-lead");

var agentRunner =
    serviceProvider.GetRequiredService<IAgentRunner>();

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

if (!instructions.Contains(roleInstructions, StringComparison.Ordinal) ||
    !instructions.Contains(skillInstructions, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Agent instructions could not be verified.");
}

const string expectedResponse = "TRAILY_CODEX_SMOKE_TEST_OK";

var request = new AgentRequest(
    AgentRole: teamLead.Role,
    TaskId: "TRAILY-SMOKE-TEST",
    Instructions: $"""
        This is a Traily runtime connectivity test.

        Reply with exactly this text:
        {expectedResponse}

        Do not inspect files or use tools.
        """,
    WorkingDirectory: rootDirectory);

var result = await agentRunner.RunAsync(request);

if (!result.Success ||
    result.Output.Trim() != expectedResponse)
{
    throw new InvalidOperationException(
        "Agent execution smoke test failed.");
}

Console.WriteLine(
    $"Traily smoke test passed. Agent: {teamLead.Role}");