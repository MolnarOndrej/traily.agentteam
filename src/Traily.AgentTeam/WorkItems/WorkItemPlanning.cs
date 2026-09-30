namespace Traily.AgentTeam.WorkItems;

public enum WorkItemExecutionPhase
{
    Planning,
    PlanningComplete
}

public enum WorkItemPlanningOutcome
{
    Ready,
    NeedsClarification
}

// CommitId identifies the actual inspected checkout revision.
// It is not necessarily the configured remote base commit.
public sealed record PlanningRepositorySnapshot(
    string RepositoryId,
    string CommitId);

public sealed record WorkItemPlanningInput(
    string TaskSnapshot,
    DateTimeOffset TaskSourceUpdatedAt,
    string EffectivePrompt,
    IReadOnlyList<PlanningRepositorySnapshot> Repositories);

public sealed record PlannedRepository(
    string RepositoryId,
    string Rationale,
    string Evidence);

public sealed record WorkItemPlanningResult(
    WorkItemPlanningOutcome Outcome,
    string Explanation,
    IReadOnlyList<PlannedRepository> SelectedRepositories);