using IncidentFlow.Api.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace IncidentFlow.Api.Data;

// A DbContext is roughly one unit of work: it describes the connection and
// configures exactly how our C# classes map onto database tables/columns.
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<IncidentParticipant> IncidentParticipants => Set<IncidentParticipant>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    public DbSet<Comment> Comments => Set<Comment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Department>(entity =>
        {
            // Two unique indexes: no two departments may share a code or a
            // name, and looking a department up by either stays fast.
            entity.HasIndex(d => d.Code).IsUnique();
            entity.HasIndex(d => d.Name).IsUnique();

            // Stored as text ("PRODUCT_AREA") rather than a number, same as
            // the incident enums below.
            entity.Property(d => d.Kind).HasConversion<string>();

            // One department has many users; every user has exactly one
            // department. DeleteBehavior.Restrict means the database will
            // REFUSE to delete a department that still has employees in it,
            // rather than silently deleting those people along with it.
            entity.HasMany(d => d.Members)
                .WithOne(u => u.Department)
                .HasForeignKey(u => u.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            // Unique and indexed: the database itself refuses a second row
            // with the same email, and lookups by email stay fast.
            entity.HasIndex(u => u.Email).IsUnique();

            entity.Property(u => u.EmploymentType).HasConversion<string>();

            // Stored as text ("DIRECTOR") for readability. Note the
            // trade-off: because the column holds words rather than the
            // enum's numbers, the DATABASE cannot sort or compare levels
            // ("DIRECTOR" < "JUNIOR" alphabetically, which is nonsense), so
            // any ranking has to happen in C# - see UserEndpoints.cs.
            entity.Property(u => u.Seniority).HasConversion<string>();

            // Not unique - this index just makes "give me everyone in the
            // Cloud department" fast once there are a lot of users.
            entity.HasIndex(u => u.DepartmentId);
        });

        // Postgres gives every row a hidden system column, xmin, holding the
        // id of the transaction that last wrote it - so it changes on every
        // update, for free, with no column of our own to maintain. Telling EF
        // to use it as a concurrency token makes every UPDATE say "...WHERE
        // xmin = <what I read>", so a write built on a stale read affects no
        // rows and EF raises DbUpdateConcurrencyException instead of silently
        // overwriting someone else's change (see the handler in Program.cs,
        // which turns that into a 409).
        //
        // Guarded by the provider check because xmin is a Postgres invention:
        // the SQLite database the tests run against has no such column, and
        // asking for it there would break every update. The practical
        // consequence is that this protection cannot be tested on SQLite -
        // see SlaMonitorTests' note on the same trade-off.
        var isPostgres = Database.IsNpgsql();

        modelBuilder.Entity<Incident>(entity =>
        {
            // Enums are stored as their text name ("HIGH") rather than the
            // underlying number - much easier to read directly in the database.
            entity.Property(i => i.Severity).HasConversion<string>();
            entity.Property(i => i.Status).HasConversion<string>();

            // Two people resolving the same incident at the same moment used
            // to produce last-write-wins plus two contradictory lines in the
            // activity feed. Now the second one is told to re-read and retry.
            if (isPostgres) UseXmin(entity);

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

            entity.HasMany(i => i.Comments)
                .WithOne(c => c.Incident)
                .HasForeignKey(c => c.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Every incident belongs to exactly one department, and a
            // department that still owns incidents cannot be deleted
            // (Restrict) - the same protection the Users table gets above.
            entity.HasOne(i => i.Department)
                .WithMany()
                .HasForeignKey(i => i.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // "Show me this department's incidents" is the main filter on the
            // list page, so the column gets an index to keep that fast.
            entity.HasIndex(i => i.DepartmentId);

            // The Incident Commander. Optional (an incident can have none),
            // and deleting a user who is commanding an incident is refused
            // rather than silently leaving it without an owner.
            entity.HasOne(i => i.Commander)
                .WithMany()
                .HasForeignKey(i => i.CommanderUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Whoever acknowledged it - an id only, no navigation property,
            // because nothing in the UI needs more than "it was acknowledged".
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(i => i.AcknowledgedBy)
                .OnDelete(DeleteBehavior.Restrict);

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

            // Same protection as Incident above: two people dragging the same
            // task through its statuses at once should not quietly lose one
            // of the changes.
            if (isPostgres) UseXmin(entity);

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

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.HasOne(c => c.User)
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Comments are always read as "this incident's conversation, in
            // order", so this is the index that query wants.
            entity.HasIndex(c => new { c.IncidentId, c.CreatedAt });
        });
    }

    // Marks one entity's hidden Postgres xmin column as its concurrency
    // token. Written once here and called from both entity configurations
    // above, so the three fiddly details below live in one place:
    //
    //   - It is a SHADOW property: there is no `Version` field on the
    //     Incident or TaskItem classes. Nothing in the app ever reads it, so
    //     adding a property for it would just be noise that invites someone
    //     to "fix" it later.
    //   - uint, because that is what a Postgres transaction id is.
    //   - ValueGeneratedOnAddOrUpdate, because Postgres - not EF - decides
    //     what goes in it. Without this EF would try to write the column,
    //     and xmin is not writable.
    //
    // Npgsql recognises the column name "xmin" as a system column, so it is
    // never included in CREATE TABLE. (This replaces the old one-liner
    // UseXminAsConcurrencyToken(), which does exactly this and is now marked
    // obsolete in favour of spelling it out.)
    private static void UseXmin<TEntity>(EntityTypeBuilder<TEntity> entity) where TEntity : class =>
        entity.Property<uint>("xmin")
            .HasColumnName("xmin")
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

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

        foreach (var entry in ChangeTracker.Entries<Comment>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = now;
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
