using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.Operations;

public sealed class OperationalIssueQuery(TrailyDbContext database)
{
    public async Task<IReadOnlyList<OperationalIssue>> GetUnresolvedAsync(
        CancellationToken cancellationToken = default)
    {
        return await database.OperationalIssues
            .AsNoTracking()
            .Where(issue => issue.ResolvedAt == null)
            .OrderBy(issue => issue.ScopeType)
            .ThenBy(issue => issue.ScopeId)
            .ThenBy(issue => issue.Capability)
            .ToListAsync(cancellationToken);
    }
}
