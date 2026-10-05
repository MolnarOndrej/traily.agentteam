using Traily.AgentTeam.Git;
using Traily.AgentTeam.Operations;

namespace Traily.AgentTeam.WorkSources;

public interface IWorkSourceAccessCheck
{
    string ProviderId { get; }

    Task<OperationalObservation> CheckConnectionAsync(
        WorkSourceConnection connection,
        CancellationToken cancellationToken = default);

    Task<OperationalObservation> CheckProjectAsync(
        WorkSourceConnection connection,
        ManagedProject project,
        CancellationToken cancellationToken = default);
}
