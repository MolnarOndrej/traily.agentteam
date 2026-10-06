using Traily.AgentTeam.Persistence;

namespace Traily.AgentTeam.WorkItems;

// Keep the ownership fence identical for planning and execution. These queries
// stay composable so EF applies the fence in the same SQL statement as a write.
internal static class WorkItemClaimQueries
{
    public static IQueryable<WorkItemJob> CurrentJob(
        this TrailyDbContext database, WorkItemClaim claim) =>
        database.WorkItemJobs.Where(job =>
            job.Id == claim.JobId &&
            job.AgentId == claim.AgentId &&
            job.CurrentAttemptId == claim.AttemptId &&
            job.Status == WorkItemJobStatus.Running);

    public static IQueryable<WorkItemExecutionAttempt> CurrentAttempt(
        this TrailyDbContext database, WorkItemClaim claim)
    {
        var currentJob = database.CurrentJob(claim);

        return database.WorkItemExecutionAttempts.Where(attempt =>
            attempt.Id == claim.AttemptId &&
            attempt.WorkItemJobId == claim.JobId &&
            currentJob.Any());
    }
}
