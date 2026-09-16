using IncidentFlow.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Data;

// A DbContext is roughly one unit of work: it describes the connection and
// configures exactly how our C# classes map onto database tables/columns.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<IncidentParticipant> IncidentParticipants => Set<IncidentParticipant>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            // Unique and indexed: the database itself refuses a second row
            // with the same email, and lookups by email stay fast.
            entity.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Incident>(entity =>
        {
            // Enums are stored as their text name ("HIGH") rather than the
            // underlying number - much easier to read directly in the database.
            entity.Property(i => i.Severity).HasConversion<string>();
            entity.Property(i => i.Status).HasConversion<string>();

            // DeleteBehavior.Cascade: deleting an incident also deletes its
            // tasks/logs/participants, so we never leave orphaned rows.
            entity.HasMany(i => i.Tasks)
                .WithOne(t => t.Incident)
                .HasForeignKey(t => t.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(i => i.Participants)
                .WithOne(p => p.Incident)
                .HasForeignKey(p => p.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(i => i.ActivityLogs)
                .WithOne(a => a.Incident)
                .HasForeignKey(a => a.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            // CreatedBy has no navigation property back to the creating User
            // (we never need "incident.Creator" in code), but we still want
            // the database itself to enforce that it points at a real user -
            // HasOne<User>() without picking a navigation property configures
            // just the foreign key constraint.
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(i => i.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.Property(t => t.Status).HasConversion<string>();

            // A task's assignee is optional and, unlike the relationships
            // above, deleting a user should NOT cascade-delete every task
            // ever assigned to them - so this one is left as the default
            // (restrict) behavior instead of Cascade.
            entity.HasOne(t => t.Assignee)
                .WithMany()
                .HasForeignKey(t => t.AssignedTo)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IncidentParticipant>(entity =>
        {
            entity.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    // EF Core has no built-in "run this function whenever a row is
    // inserted/updated" column default, so we reproduce that behavior by
    // overriding SaveChanges: right before anything is written to the
    // database, stamp CreatedAt on new rows and UpdatedAt on new-or-changed
    // rows that have it.
    public override int SaveChanges()
    {
        StampTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampTimestamps()
    {
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<ActivityLog>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<IncidentParticipant>())
        {
            if (entry.State == EntityState.Added) entry.Entity.JoinedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<Incident>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
        }

        foreach (var entry in ChangeTracker.Entries<TaskItem>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
            if (entry.State is EntityState.Added or EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
    }
}
