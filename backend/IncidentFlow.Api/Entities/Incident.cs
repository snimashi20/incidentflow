namespace IncidentFlow.Api.Entities;

public class Incident
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Severity Severity { get; set; }

    public IncidentStatus Status { get; set; } = IncidentStatus.OPEN;

    // Which user created this incident. EF Core turns this into a foreign
    // key column that the database enforces must point at a real row in the
    // "Users" table.
    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation properties: EF Core uses these to run the right JOIN/SELECT
    // when we ask for an incident "with its tasks", instead of us writing SQL
    // by hand. Configured with cascade delete in AppDbContext, so deleting an
    // incident also deletes its tasks/logs/participants - no orphaned rows.
    public List<TaskItem> Tasks { get; set; } = [];

    public List<IncidentParticipant> Participants { get; set; } = [];

    public List<ActivityLog> ActivityLogs { get; set; } = [];
}
