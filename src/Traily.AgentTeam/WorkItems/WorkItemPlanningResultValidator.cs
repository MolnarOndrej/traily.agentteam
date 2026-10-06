namespace Traily.AgentTeam.WorkItems;

// Result validity depends on the saved inspection input. Repository permission
// checks remain in the service because they must use current database state.
internal static class WorkItemPlanningResultValidator
{
    public static WorkItemStopReason? GetStopReason(
        WorkItemPlanningResult result, WorkItemPlanningInput input)
    {
        var selected = result.SelectedRepositories;

        if (!Enum.IsDefined(result.Outcome) ||
            string.IsNullOrWhiteSpace(result.Explanation) ||
            selected is null ||
            (result.Outcome == WorkItemPlanningOutcome.Ready && selected.Count == 0))
        {
            return WorkItemStopReason.InvalidPlanningResult;
        }

        var inspectedIds = input.Repositories
            .Select(repository => repository.RepositoryId)
            .ToHashSet(StringComparer.Ordinal);
        var selectedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var repository in selected)
        {
            if (repository is null ||
                string.IsNullOrWhiteSpace(repository.RepositoryId) ||
                string.IsNullOrWhiteSpace(repository.Rationale) ||
                string.IsNullOrWhiteSpace(repository.Evidence) ||
                !inspectedIds.Contains(repository.RepositoryId) ||
                !selectedIds.Add(repository.RepositoryId))
            {
                return WorkItemStopReason.InvalidPlanningResult;
            }
        }

        return result.Outcome == WorkItemPlanningOutcome.NeedsClarification
            ? WorkItemStopReason.PlanningUncertain
            : null;
    }
}
