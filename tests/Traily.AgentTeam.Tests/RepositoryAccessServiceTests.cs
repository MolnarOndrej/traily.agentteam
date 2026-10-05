using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Persistence;
using Traily.AgentTeam.WorkSources;

namespace Traily.AgentTeam.Tests;

public sealed class RepositoryAccessServiceTests
{
    [Fact]
    public async Task ReturnsOnlyGrantedRepositoriesForSourceAndActiveAgent()
    {
        await using var connection = new SqliteConnection(
            "Data Source=:memory:;Foreign Keys=True");

        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<TrailyDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var database = new TrailyDbContext(options);
        await database.Database.EnsureCreatedAsync();

        var now = new DateTimeOffset(
            2026, 9, 30, 0, 0, 0, TimeSpan.Zero);

        database.ManagedProjects.Add(new ManagedProject
        {
            SourceId = "youtrack-stepin",
            Name = "StepIn",
            WorkSourceConnectionId = "test-youtrack",
            ExternalProjectId = "0-1",
            WorkSourceConnection = new WorkSourceConnection
            {
                Id = "test-youtrack",
                ProviderId = "YouTrack",
                BaseUrl = "https://example.invalid",
                CreatedAt = now,
                UpdatedAt = now
            },
            CreatedAt = now,
            UpdatedAt = now
        });

        database.GitRepositories.AddRange(
            new GitRepository
            {
                Id = "stepin-api",
                SourceId = "youtrack-stepin",
                Name = "StepIn API",
                RemoteUrl =
                    "https://github.com/MolnarOndrej/StepIn.Api.git",
                BaseBranch = "master",
                LocalPathOverride = @"C:\Git\stepin.api",
                CreatedAt = now,
                UpdatedAt = now
            },
            new GitRepository
            {
                Id = "stepin-ui",
                SourceId = "youtrack-stepin",
                Name = "StepIn UI",
                RemoteUrl =
                    "https://github.com/MolnarOndrej/StepIn.UI.git",
                BaseBranch = "master",
                CreatedAt = now,
                UpdatedAt = now
            });

        database.AgentProfiles.AddRange(
            new AgentProfile
            {
                Id = "team-lead",
                Name = "Team Lead",
                Instructions = "Analyze tasks.",
                IsEnabled = true,
                MaxConcurrentJobs = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new AgentProfile
            {
                Id = "react-expert",
                Name = "React Expert",
                Instructions = "Implement React tasks.",
                IsEnabled = true,
                MaxConcurrentJobs = 1,
                CreatedAt = now,
                UpdatedAt = now
            });

        database.AgentRepositoryAccesses.AddRange(
            new AgentRepositoryAccess
            {
                AgentId = "team-lead",
                RepositoryId = "stepin-api",
                Level = RepositoryAccessLevel.Read
            },
            new AgentRepositoryAccess
            {
                AgentId = "team-lead",
                RepositoryId = "stepin-ui",
                Level = RepositoryAccessLevel.Write
            });

        await database.SaveChangesAsync();

        var service = new RepositoryAccessService(database);

        var repositories =
            await service.GetAccessibleRepositoriesAsync(
                "youtrack-stepin",
                "team-lead");

        Assert.Collection(
            repositories,
            api =>
            {
                Assert.Equal("stepin-api", api.RepositoryId);
                Assert.Equal(RepositoryAccessLevel.Read, api.Level);
                Assert.False(api.CanWrite);
                Assert.Equal(
                    @"C:\Git\stepin.api",
                    api.LocalPathOverride);
            },
            ui =>
            {
                Assert.Equal("stepin-ui", ui.RepositoryId);
                Assert.Equal(RepositoryAccessLevel.Write, ui.Level);
                Assert.True(ui.CanWrite);
            });

        Assert.Empty(
            await service.GetAccessibleRepositoriesAsync(
                "youtrack-stepin",
                "react-expert"));

        Assert.Empty(
            await service.GetAccessibleRepositoriesAsync(
                "another-source",
                "team-lead"));

        var teamLead = await database.AgentProfiles
            .SingleAsync(agent => agent.Id == "team-lead");

        teamLead.IsEnabled = false;
        await database.SaveChangesAsync();

        Assert.Empty(
            await service.GetAccessibleRepositoriesAsync(
                "youtrack-stepin",
                "team-lead"));
    }
}
