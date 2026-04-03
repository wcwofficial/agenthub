using Microsoft.EntityFrameworkCore;

namespace AgentHub.Api;

public class AgentHubDbContext(DbContextOptions<AgentHubDbContext> options) : DbContext(options)
{
    public DbSet<AgentEntity> Agents => Set<AgentEntity>();
    public DbSet<AgentSkillEntity> AgentSkills => Set<AgentSkillEntity>();
    public DbSet<TaskEntity> Tasks => Set<TaskEntity>();
    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<MessageEntity> Messages => Set<MessageEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ApiKey).IsRequired();
            entity.HasIndex(x => x.ApiKey).IsUnique();
            entity.Property(x => x.Name).IsRequired();
            entity.Property(x => x.Roles).IsRequired();
        });

        modelBuilder.Entity<TaskEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).IsRequired();
        });

        modelBuilder.Entity<AgentSkillEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Agent)
                .WithMany(x => x.AgentSkills)
                .HasForeignKey(x => x.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.AgentId, x.Skill }).IsUnique();
        });

        modelBuilder.Entity<ConversationEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ParticipantAgentIds).IsRequired();
        });

        modelBuilder.Entity<MessageEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Body).IsRequired();
        });
    }
}
