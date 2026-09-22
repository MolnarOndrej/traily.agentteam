using Microsoft.EntityFrameworkCore;
using Traily.AgentTeam.Agents;

namespace Traily.AgentTeam.Persistence;

public sealed class TrailyDbContext(
    DbContextOptions<TrailyDbContext> options)
    : DbContext(options)
{
    public DbSet<AgentProfile> AgentProfiles => Set<AgentProfile>();

    public DbSet<AgentSkill> AgentSkills => Set<AgentSkill>();

    public DbSet<AgentExternalIdentity> AgentExternalIdentities =>
        Set<AgentExternalIdentity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureAgentProfile(modelBuilder);
        ConfigureAgentSkill(modelBuilder);
        ConfigureAgentExternalIdentity(modelBuilder);
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