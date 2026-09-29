using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Tests;

public sealed class WorkItemClaimServiceTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConcurrentClaimsRespectAgentLimitAndCreateAttempts()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"traily-claims-{Guid.NewGuid():N}.db");

        try
        {
            await SeedAsync(path);

            var results = await Task.WhenAll(
                ClaimAsync(path),
                ClaimAsync(path),
                ClaimAsync(path));

            var claims = results
                .OfType<WorkItemClaim>()
                .ToArray();

            Assert.Equal(2, claims.Length);
            Assert.Equal(
                2,
                claims.Select(claim => claim.JobId)
                    .Distinct()
                    .Count());
            Assert.Equal(
                2,
                claims.Select(claim => claim.AttemptId)
                    .Distinct()
                    .Count());

            Assert.Null(await ClaimAsync(path));

            await using var database = OpenDatabase(path);

            var runningJobs = await database.WorkItemJobs
                .AsNoTracking()
                .Where(job =>
                    job.Status == WorkItemJobStatus.Running)
                .ToListAsync();

            var attempts = await database
                .WorkItemExecutionAttempts
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(2, runningJobs.Count);
            Assert.Equal(2, attempts.Count);
            Assert.Equal(
                1,
                await database.WorkItemJobs.CountAsync(
                    job => job.Status ==
                        WorkItemJobStatus.Queued));

            foreach (var claim in claims)
            {
                Assert.Equal("team-lead", claim.AgentId);

                var job = Assert.Single(
                    runningJobs,
                    entry => entry.Id == claim.JobId);

                var attempt = Assert.Single(
                    attempts,
                    entry => entry.Id == claim.AttemptId);

                Assert.Equal(
                    claim.AttemptId,
                    job.CurrentAttemptId);
                Assert.Equal(
                    claim.JobId,
                    attempt.WorkItemJobId);
                Assert.Null(attempt.StartedAt);
                Assert.Null(attempt.ProviderId);
                Assert.Null(attempt.ProviderSessionId);
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static async Task<WorkItemClaim?> ClaimAsync(
        string path)
    {
        await using var database = OpenDatabase(path);

        var service = new WorkItemClaimService(
            database,
            TimeProvider.System);

        return await service.ClaimNextAsync();
    }

    private static TrailyDbContext OpenDatabase(string path)
    {
        var options = new DbContextOptionsBuilder<TrailyDbContext>()
            .UseSqlite(
                new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    ForeignKeys = true,
                    DefaultTimeout = 30
                }.ToString())
            .Options;

        return new TrailyDbContext(options);
    }

    private static async Task SeedAsync(string path)
    {
        await using var database = OpenDatabase(path);
        await database.Database.EnsureCreatedAsync();

        database.AgentProfiles.Add(new AgentProfile
        {
            Id = "team-lead",
            Name = "Team Lead",
            Instructions = "Analyze tasks.",
            IsEnabled = true,
            MaxConcurrentJobs = 2,
            CreatedAt = Start,
            UpdatedAt = Start
        });

        for (var number = 1; number <= 3; number++)
        {
            database.WorkItemJobs.Add(new WorkItemJob
            {
                Id = Guid.NewGuid(),
                SourceId = "test-source",
                ExternalWorkItemId = $"issue-{number}",
                WorkItemReference = $"TEST-{number}",
                Title = $"Test job {number}",
                ExternalAssigneeId = "user-1",
                AgentId = "team-lead",
                Status = WorkItemJobStatus.Queued,
                SourceUpdatedAt = Start,
                CreatedAt = Start.AddSeconds(number),
                UpdatedAt = Start
            });
        }

        await database.SaveChangesAsync();
    }
}