using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;

namespace Traily.AgentTeam.Persistence;

public sealed class DatabaseAgentCatalog : IAgentCatalog
{
    private readonly TrailyDbContext _dbContext;

    public DatabaseAgentCatalog(TrailyDbContext dbContext)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        _dbContext = dbContext;
    }

    public async Task<AgentDefinition> GetRequiredAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var profile = await _dbContext.AgentProfiles
            .AsNoTracking()
            .Include(agent => agent.Skills)
            .SingleOrDefaultAsync(
                agent => agent.Id == agentId,
                cancellationToken);

        if (profile is null)
        {
            throw new KeyNotFoundException(
                $"Agent '{agentId}' is not registered.");
        }

        if (profile.DeletionRequestedAt is not null)
        {
            throw new InvalidOperationException(
                $"Agent '{agentId}' is marked for deletion.");
        }

        if (!profile.IsEnabled)
        {
            throw new InvalidOperationException(
                $"Agent '{agentId}' is disabled.");
        }

        var skills = profile.Skills
            .OrderBy(
                skill => skill.SkillId,
                StringComparer.Ordinal)
            .Select(skill => new AgentSkillDefinition(
                Id: skill.SkillId,
                Instructions: skill.Instructions))
            .ToArray();

        return new AgentDefinition(
            Id: profile.Id,
            Name: profile.Name,
            Instructions: profile.Instructions,
            MaxConcurrentJobs: profile.MaxConcurrentJobs,
            Skills: skills);
    }

    public async Task<IReadOnlyList<AgentSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.AgentProfiles
            .AsNoTracking()
            .Where(agent => agent.DeletionRequestedAt == null)
            .OrderBy(agent => agent.Id)
            .Select(agent => new AgentSummary(
                agent.Id,
                agent.Name,
                agent.IsEnabled,
                agent.MaxConcurrentJobs,
                agent.Skills.Count))
            .ToArrayAsync(cancellationToken);
    }
}