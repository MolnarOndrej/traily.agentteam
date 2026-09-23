using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAgentProfile(modelBuilder);
        ConfigureAgentSkill(modelBuilder);
        ConfigureAgentExternalIdentity(modelBuilder);
        ConfigureWorkItemJob(modelBuilder);
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

    private static void ConfigureWorkItemJob(
    ModelBuilder modelBuilder)
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

    private static void ConfigureAgentExternalIdentity(
        ModelBuilder modelBuilder)
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
}