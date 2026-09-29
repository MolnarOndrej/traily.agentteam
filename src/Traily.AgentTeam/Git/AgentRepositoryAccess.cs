using Traily.AgentTeam.Agents;

namespace Traily.AgentTeam.Git;

public sealed class AgentRepositoryAccess
{
    public string AgentId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public RepositoryAccessLevel Level { get; set; }

    public AgentProfile Agent { get; set; } = null!;
    public GitRepository Repository { get; set; } = null!;
}