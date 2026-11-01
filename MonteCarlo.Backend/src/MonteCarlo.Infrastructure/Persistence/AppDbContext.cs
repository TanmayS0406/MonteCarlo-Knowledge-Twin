using Microsoft.EntityFrameworkCore;
using MonteCarlo.Application.Abstractions;
using MonteCarlo.Domain.Common;
using MonteCarlo.Domain.Entities;

namespace MonteCarlo.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IKnowledgeStore
{
    public DbSet<Project> ProjectSet => Set<Project>();
    public DbSet<Person> PersonSet => Set<Person>();
    public DbSet<WorkItem> WorkItemSet => Set<WorkItem>();
    public DbSet<WorkItemDependency> DependencySet => Set<WorkItemDependency>();
    public DbSet<Meeting> MeetingSet => Set<Meeting>();
    public DbSet<SpeakerTurn> TurnSet => Set<SpeakerTurn>();

    // ---- IKnowledgeStore ----
    public IQueryable<Project> Projects => Set<Project>();
    public IQueryable<Person> People => Set<Person>();
    public IQueryable<WorkItem> WorkItems => Set<WorkItem>().Include(w => w.Dependencies);
    public IQueryable<WorkItemDependency> Dependencies => Set<WorkItemDependency>();
    public IQueryable<Meeting> Meetings => Set<Meeting>();
    public IQueryable<SpeakerTurn> SpeakerTurns => Set<SpeakerTurn>();
    public IQueryable<Evidence> Evidence => Set<Evidence>();
    public IQueryable<Decision> Decisions => Set<Decision>();
    public IQueryable<ActionItem> ActionItems => Set<ActionItem>();
    public IQueryable<Blocker> Blockers => Set<Blocker>();
    public IQueryable<Suggestion> Suggestions => Set<Suggestion>();
    public IQueryable<CodeEvent> CodeEvents => Set<CodeEvent>();
    public IQueryable<ExtractionLog> ExtractionLogs => Set<ExtractionLog>();
    public IQueryable<SimulationRun> SimulationRuns => Set<SimulationRun>();
    public IQueryable<AuditEntry> AuditEntries => Set<AuditEntry>();
    public IQueryable<TenantSettings> Settings => Set<TenantSettings>();

    void IKnowledgeStore.Add<T>(T entity) => Add(entity);
    void IKnowledgeStore.Remove<T>(T entity) => Remove(entity);
    public Task<List<T>> ListAsync<T>(IQueryable<T> query, CancellationToken ct = default) => query.ToListAsync(ct);
    public Task<T?> FirstOrDefaultAsync<T>(IQueryable<T> query, CancellationToken ct = default) => query.FirstOrDefaultAsync(ct);
    public Task<int> CountAsync<T>(IQueryable<T> query, CancellationToken ct = default) => query.CountAsync(ct);

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<WorkItemStatus>().HaveConversion<string>().HaveMaxLength(24);
        b.Properties<WorkRole>().HaveConversion<string>().HaveMaxLength(24);
        b.Properties<SourceType>().HaveConversion<string>().HaveMaxLength(24);
        b.Properties<SuggestionType>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<SuggestionOutcome>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<ActionItemState>().HaveConversion<string>().HaveMaxLength(16);
        b.Properties<CodeEventType>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<MeetingSource>().HaveConversion<string>().HaveMaxLength(24);
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Project>(e =>
        {
            e.ToTable("Projects");
            e.Property(x => x.Key).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.CurrentRelease).HasMaxLength(20);
            e.HasIndex(x => x.Key).IsUnique();
        });

        mb.Entity<Person>(e =>
        {
            e.ToTable("People");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.ExternalUserId).HasMaxLength(128);
            e.Property(x => x.Skills).HasMaxLength(500);
        });

        mb.Entity<WorkItem>(e =>
        {
            e.ToTable("WorkItems");
            e.Property(x => x.ExternalRef).HasMaxLength(50);
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Release).HasMaxLength(20);
            e.HasIndex(x => new { x.ProjectId, x.ExternalRef }).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Person>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Dependencies).WithOne().HasForeignKey(d => d.WorkItemId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<WorkItemDependency>(e =>
        {
            e.ToTable("WorkItemDependencies");
            e.HasKey(x => new { x.WorkItemId, x.DependsOnId });
            e.HasOne<WorkItem>().WithMany().HasForeignKey(x => x.DependsOnId).OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<Meeting>(e =>
        {
            e.ToTable("Meetings");
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.ExternalId).HasMaxLength(400);
            e.HasIndex(x => x.ExternalId);
            e.HasIndex(x => new { x.ProjectId, x.StartedAt });
            e.HasMany(x => x.Turns).WithOne().HasForeignKey(t => t.MeetingId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<SpeakerTurn>(e =>
        {
            e.ToTable("SpeakerTurns");
            e.Property(x => x.SpeakerName).HasMaxLength(200);
            e.HasIndex(x => new { x.MeetingId, x.Sequence });
        });

        mb.Entity<Evidence>(e =>
        {
            e.ToTable("Evidence");
            e.Property(x => x.SourceId).HasMaxLength(400);
            e.Property(x => x.SpeakerName).HasMaxLength(200);
            e.Property(x => x.QuotedText).HasMaxLength(1000);
            e.HasIndex(x => x.MeetingId);
        });

        mb.Entity<Decision>(e =>
        {
            e.ToTable("Decisions");
            e.Property(x => x.Statement).HasMaxLength(1000);
            e.Property(x => x.DecidedByName).HasMaxLength(200);
            e.Property(x => x.Rationale).HasMaxLength(1000);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.MeetingId);
            e.HasIndex(x => x.AffectedWorkItemId);
        });

        mb.Entity<ActionItem>(e =>
        {
            e.ToTable("ActionItems");
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.OwnerName).HasMaxLength(200);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.MeetingId);
            e.HasIndex(x => x.LinkedWorkItemId);
        });

        mb.Entity<Blocker>(e =>
        {
            e.ToTable("Blockers");
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.RaisedByName).HasMaxLength(200);
            e.HasIndex(x => x.ProjectId);
            e.HasIndex(x => x.BlockingWorkItemId);
        });

        mb.Entity<Suggestion>(e =>
        {
            e.ToTable("Suggestions");
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Detail).HasMaxLength(2000);
            e.Property(x => x.Fingerprint).HasMaxLength(200);
            e.Property(x => x.ProposedTitle).HasMaxLength(500);
            e.Property(x => x.ProposedRelease).HasMaxLength(20);
            e.Property(x => x.ReviewedBy).HasMaxLength(200);
            e.Property(x => x.ReviewNote).HasMaxLength(1000);
            e.HasIndex(x => new { x.ProjectId, x.Fingerprint }).IsUnique();
            e.HasIndex(x => new { x.ProjectId, x.Outcome });
        });

        mb.Entity<CodeEvent>(e =>
        {
            e.ToTable("CodeEvents");
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Author).HasMaxLength(200);
            e.Property(x => x.Branch).HasMaxLength(300);
            e.Property(x => x.TicketRefs).HasMaxLength(400);
            e.Property(x => x.Url).HasMaxLength(1000);
            e.HasIndex(x => new { x.ProjectId, x.OccurredAt });
        });

        mb.Entity<ExtractionLog>(e =>
        {
            e.ToTable("ExtractionLogs");
            e.Property(x => x.Extractor).HasMaxLength(100);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.PromptVersion).HasMaxLength(50);
            e.HasIndex(x => x.MeetingId);
        });

        mb.Entity<SimulationRun>(e =>
        {
            e.ToTable("SimulationRuns");
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.ProjectId, x.CreatedAt });
        });

        mb.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.Property(x => x.Actor).HasMaxLength(200);
            e.Property(x => x.Action).HasMaxLength(100);
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Details).HasMaxLength(2000);
            e.HasIndex(x => x.CreatedAt);
        });

        mb.Entity<TenantSettings>(e =>
        {
            e.ToTable("Settings");
            e.Property(x => x.TeamName).HasMaxLength(200);
            e.Property(x => x.DataRegion).HasMaxLength(100);
        });
    }
}
