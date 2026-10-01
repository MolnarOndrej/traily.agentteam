using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;
using Traily.AgentTeam.Git;
using Traily.AgentTeam.Operations;
using Traily.AgentTeam.WorkItems;

namespace Traily.AgentTeam.Persistence;

public sealed class TrailyDbContext(
    DbContextOptions<TrailyDbContext> options)
    : DbContext(options)
{
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();

    public DbSet<AgentSkill> AgentSkills => Set<AgentSkill>();

    public DbSet<AgentExternalIdentity> AgentExternalIdentities =>
        Set<AgentExternalIdentity>();

    public DbSet<WorkItemJob> WorkItemJobs =>
        Set<WorkItemJob>();

    public DbSet<WorkItemExecutionAttempt> WorkItemExecutionAttempts =>
        Set<WorkItemExecutionAttempt>();

    public DbSet<ManagedProject> ManagedProjects =>
        Set<ManagedProject>();

    public DbSet<GitRepository> GitRepositories =>
        Set<GitRepository>();

    public DbSet<AgentRepositoryAccess> AgentRepositoryAccesses =>
        Set<AgentRepositoryAccess>();

    public DbSet<OperationalIssue> OperationalIssues =>
        Set<OperationalIssue>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAgentProfile(modelBuilder);
        ConfigureAgentSkill(modelBuilder);
        ConfigureAgentExternalIdentity(modelBuilder);
        ConfigureWorkItemJob(modelBuilder);
        ConfigureWorkItemExecutionAttempt(modelBuilder);
        ConfigureManagedProject(modelBuilder);
        ConfigureGitRepository(modelBuilder);
        ConfigureAgentRepositoryAccess(modelBuilder);
        ConfigureOperationalIssue(modelBuilder);
    }

    private static void ConfigureAgentProfile(ModelBuilder modelBuilder)
    {
        var profile = modelBuilder.Entity<AgentProfile>();

        profile.ToTable(
            "AgentProfiles",
            table => table.HasCheckConstraint(
                "CK_AgentProfiles_MaxConcurrentJobs",
                "\"MaxConcurrentJobs\" > 0"));

        profile.HasKey(agent => agent.Id);

        profile.Property(agent => agent.Id)
            .HasMaxLength(100)
            .ValueGeneratedNever();

        profile.Property(agent => agent.Name)
            .HasMaxLength(200)
            .IsRequired();

        profile.Property(agent => agent.Instructions)
            .IsRequired();

        profile.Property(agent => agent.IsEnabled)
            .IsRequired();

        profile.Property(agent => agent.MaxConcurrentJobs)
            .IsRequired();

        profile.Property(agent => agent.CreatedAt)
            .IsRequired();

        profile.Property(agent => agent.UpdatedAt)
            .IsRequired();

        profile.Property(agent => agent.DeletionRequestedAt);

        profile.HasIndex(agent => agent.DeletionRequestedAt);
    }

    private static void ConfigureAgentSkill(ModelBuilder modelBuilder)
    {
        var skill = modelBuilder.Entity<AgentSkill>();

        skill.ToTable("AgentSkills");

        skill.HasKey(agentSkill => agentSkill.Id);

        skill.Property(agentSkill => agentSkill.AgentId)
            .HasMaxLength(100)
            .IsRequired();

        skill.Property(agentSkill => agentSkill.SkillId)
            .HasMaxLength(100)
            .IsRequired();

        skill.Property(agentSkill => agentSkill.Instructions)
            .IsRequired();

        skill.HasIndex(agentSkill => new
        {
            agentSkill.AgentId,
            agentSkill.SkillId
        })
            .IsUnique();

        skill.HasOne(agentSkill => agentSkill.Agent)
            .WithMany(agent => agent.Skills)
            .HasForeignKey(agentSkill => agentSkill.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        skill.Property(agentSkill => agentSkill.CreatedAt)
            .IsRequired();

        skill.Property(agentSkill => agentSkill.UpdatedAt)
            .IsRequired();
    }

    private static void ConfigureAgentExternalIdentity(ModelBuilder modelBuilder)
    {
        var identity = modelBuilder.Entity<AgentExternalIdentity>();

        identity.ToTable("AgentExternalIdentities");

        identity.HasKey(externalIdentity => externalIdentity.Id);

        identity.Property(externalIdentity => externalIdentity.AgentId)
            .HasMaxLength(100)
            .IsRequired();

        identity.Property(externalIdentity => externalIdentity.SourceId)
            .HasMaxLength(100)
            .IsRequired();

        identity.Property(externalIdentity =>
                externalIdentity.ExternalUserId)
            .HasMaxLength(200)
            .IsRequired();

        identity.Property(externalIdentity => externalIdentity.Login)
            .HasMaxLength(200)
            .IsRequired();

        identity.Property(externalIdentity => externalIdentity.DisplayName)
            .HasMaxLength(200);

        identity.HasIndex(externalIdentity => new
        {
            externalIdentity.SourceId,
            externalIdentity.ExternalUserId
        })
            .IsUnique();

        identity.HasIndex(externalIdentity => new
        {
            externalIdentity.SourceId,
            externalIdentity.Login
        });

        identity.HasOne(externalIdentity => externalIdentity.Agent)
            .WithMany(agent => agent.ExternalIdentities)
            .HasForeignKey(externalIdentity =>
                externalIdentity.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        identity.Property(externalIdentity =>
                externalIdentity.CreatedAt)
            .IsRequired();

        identity.Property(externalIdentity =>
                externalIdentity.UpdatedAt)
            .IsRequired();
    }

    private static void ConfigureWorkItemJob(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<WorkItemJob>();

        job.ToTable("WorkItemJobs");

        job.HasKey(workItemJob => workItemJob.Id);

        job.Property(workItemJob => workItemJob.Id)
            .ValueGeneratedNever();

        job.Property(workItemJob => workItemJob.SourceId)
            .HasMaxLength(100)
            .IsRequired();

        job.Property(workItemJob =>
                workItemJob.ExternalWorkItemId)
            .HasMaxLength(200)
            .IsRequired();

        job.Property(workItemJob =>
                workItemJob.WorkItemReference)
            .HasMaxLength(200)
            .IsRequired();

        job.Property(workItemJob => workItemJob.Title)
            .HasMaxLength(500)
            .IsRequired();

        job.Property(workItemJob =>
                workItemJob.ExternalAssigneeId)
            .HasMaxLength(200)
            .IsRequired();

        job.Property(workItemJob => workItemJob.AgentId)
            .HasMaxLength(100)
            .IsRequired();

        job.Property(workItemJob => workItemJob.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        job.Property(workItemJob => workItemJob.CurrentAttemptId);

        job.Property(workItemJob =>
                workItemJob.SourceUpdatedAt)
            .IsRequired();

        job.Property(workItemJob => workItemJob.CreatedAt)
            .IsRequired();

        job.Property(workItemJob => workItemJob.UpdatedAt)
            .IsRequired();

        job.HasIndex(workItemJob => new
        {
            workItemJob.SourceId,
            workItemJob.ExternalWorkItemId
        })
            .IsUnique();

        job.HasIndex(workItemJob => new
        {
            workItemJob.AgentId,
            workItemJob.Status
        });

        job.HasIndex(workItemJob => new
        {
            workItemJob.Status,
            workItemJob.CreatedAt
        });

        job.HasOne(workItemJob => workItemJob.Agent)
            .WithMany()
            .HasForeignKey(workItemJob => workItemJob.AgentId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureWorkItemExecutionAttempt(ModelBuilder modelBuilder)
    {
        var attempt = modelBuilder.Entity<WorkItemExecutionAttempt>();

        attempt.ToTable("WorkItemExecutionAttempts");

        attempt.HasKey(entry => entry.Id);

        attempt.Property(entry => entry.Id)
            .ValueGeneratedNever();

        attempt.Property(entry => entry.WorkItemJobId)
            .IsRequired();

        attempt.Property(entry => entry.CreatedAt)
            .IsRequired();

        attempt.Property(entry => entry.StartedAt);
        attempt.Property(entry => entry.FinishedAt);

        attempt.Property(entry => entry.ProviderId)
            .HasMaxLength(100);

        attempt.Property(entry => entry.ProviderSessionId)
            .HasMaxLength(300);

        attempt.Property(entry => entry.TaskSnapshot);

        attempt.Property(entry => entry.WorkingDirectory)
            .HasMaxLength(2000);

        attempt.Property(entry => entry.StopReason)
            .HasConversion<string>()
            .HasMaxLength(100);

        attempt.Property(entry => entry.TaskSourceUpdatedAt);

        attempt.Property(entry => entry.Phase)
            .HasConversion<string>()
            .HasMaxLength(50);

        attempt.Property(entry => entry.PlanningInputJson);
        attempt.Property(entry => entry.PlanningResultJson);
        attempt.Property(entry => entry.PlanningCompletedAt);

        attempt.HasIndex(entry => entry.WorkItemJobId);

        attempt.HasOne(entry => entry.Job)
            .WithMany()
            .HasForeignKey(entry => entry.WorkItemJobId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureManagedProject(ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<ManagedProject>();

        project.ToTable("ManagedProjects");
        project.HasKey(entry => entry.SourceId);

        project.Property(entry => entry.SourceId)
            .HasMaxLength(100)
            .ValueGeneratedNever();

        project.Property(entry => entry.Name)
            .HasMaxLength(200)
            .IsRequired();

        project.Property(entry => entry.CreatedAt).IsRequired();
        project.Property(entry => entry.UpdatedAt).IsRequired();
    }

    private static void ConfigureGitRepository(ModelBuilder modelBuilder)
    {
        var repository = modelBuilder.Entity<GitRepository>();

        repository.ToTable("GitRepositories");
        repository.HasKey(entry => entry.Id);

        repository.Property(entry => entry.Id)
            .HasMaxLength(100)
            .ValueGeneratedNever();

        repository.Property(entry => entry.SourceId)
            .HasMaxLength(100)
            .IsRequired();

        repository.Property(entry => entry.Name)
            .HasMaxLength(200)
            .IsRequired();

        repository.Property(entry => entry.RemoteUrl)
            .HasMaxLength(2000)
            .IsRequired();

        repository.Property(entry => entry.BaseBranch)
            .HasMaxLength(200)
            .IsRequired();

        repository.Property(entry => entry.LocalPathOverride)
            .HasMaxLength(2000);

        repository.Property(entry => entry.CreatedAt).IsRequired();
        repository.Property(entry => entry.UpdatedAt).IsRequired();

        repository.HasIndex(entry => entry.SourceId);

        repository.HasOne(entry => entry.Project)
            .WithMany()
            .HasForeignKey(entry => entry.SourceId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAgentRepositoryAccess(
        ModelBuilder modelBuilder)
    {
        var access = modelBuilder.Entity<AgentRepositoryAccess>();

        access.ToTable("AgentRepositoryAccesses");

        access.HasKey(entry => new
        {
            entry.AgentId,
            entry.RepositoryId
        });

        access.Property(entry => entry.AgentId)
            .HasMaxLength(100);

        access.Property(entry => entry.RepositoryId)
            .HasMaxLength(100);

        access.Property(entry => entry.Level)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        access.HasIndex(entry => entry.RepositoryId);

        access.HasOne(entry => entry.Agent)
            .WithMany()
            .HasForeignKey(entry => entry.AgentId)
            .OnDelete(DeleteBehavior.Restrict);

        access.HasOne(entry => entry.Repository)
            .WithMany()
            .HasForeignKey(entry => entry.RepositoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureOperationalIssue(
    ModelBuilder modelBuilder)
    {
        var issue = modelBuilder.Entity<OperationalIssue>();

        issue.ToTable("OperationalIssues");
        issue.HasKey(entry => entry.Id);

        issue.Property(entry => entry.Id)
            .ValueGeneratedNever();

        issue.Property(entry => entry.ScopeType)
            .HasMaxLength(100)
            .IsRequired();

        issue.Property(entry => entry.ScopeId)
            .HasMaxLength(200)
            .IsRequired();

        issue.Property(entry => entry.Capability)
            .HasMaxLength(100)
            .IsRequired();

        issue.Property(entry => entry.Availability)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        issue.Property(entry => entry.ReasonCode)
            .HasMaxLength(100)
            .IsRequired();

        issue.Property(entry => entry.Message)
            .HasMaxLength(1000)
            .IsRequired();

        issue.Property(entry => entry.FirstObservedAt)
            .IsRequired();

        issue.Property(entry => entry.LastObservedAt)
            .IsRequired();

        issue.Property(entry => entry.ObservationCount)
            .IsRequired();

        issue.HasIndex(entry => new
        {
            entry.ScopeType,
            entry.ScopeId,
            entry.Capability
        })
            .IsUnique()
            .HasFilter("\"ResolvedAt\" IS NULL");
    }
}