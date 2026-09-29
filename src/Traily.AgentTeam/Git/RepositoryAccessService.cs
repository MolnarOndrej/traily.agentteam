using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.Git;

public sealed record AccessibleRepository(
    string RepositoryId,
    string Name,
    string RemoteUrl,
    string BaseBranch,
    string? LocalPathOverride,
    RepositoryAccessLevel Level)
{
    public bool CanWrite => Level == RepositoryAccessLevel.Write;
}

public sealed class RepositoryAccessService(TrailyDbContext database)
{
    public Task<List<AccessibleRepository>> GetAccessibleRepositoriesAsync(
        string sourceId,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        return database.AgentRepositoryAccesses
            .AsNoTracking()
            .Where(access =>
                access.AgentId == agentId &&
                access.Agent.IsEnabled &&
                access.Agent.DeletionRequestedAt == null &&
                access.Repository.SourceId == sourceId)
            .OrderBy(access => access.RepositoryId)
            .Select(access => new AccessibleRepository(
                access.RepositoryId,
                access.Repository.Name,
                access.Repository.RemoteUrl,
                access.Repository.BaseBranch,
                access.Repository.LocalPathOverride,
                access.Level))
            .ToListAsync(cancellationToken);
    }
}