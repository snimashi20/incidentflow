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

    // Which department owns this incident - the team whose problem it is.
    //
    // This is what makes escalation possible at all: "get me someone senior"
    // is a meaningless request until the system knows *senior in which
    // team*. A failing production database escalates to the Cloud engineers,
    // not to whoever happens to be the most senior person in the company.
    public int DepartmentId { get; set; }

    // The Incident Commander: the single person accountable for driving this
    // incident to resolution. Nullable because an incident starts without
    // one - somebody has to take the role, and during the first minutes of
    // an outage nobody has yet.
    //
    // Why have this at all when there is already a participant list: during
    // a real incident, "everyone is responsible" reliably means nobody is.
    // One named owner on the page is the cheapest fix for that.
    public int? CommanderUserId { get; set; }

    // When a human first took this incident on, and who. Null means nobody
    // has acknowledged it yet - which is what the SLA clock is watching for
    // (see Common/SlaPolicy.cs).
    public DateTime? AcknowledgedAt { get; set; }

    public int? AcknowledgedBy { get; set; }

    // When somebody escalated this to the department's senior people. Null
    // means it has not been escalated.
    public DateTime? EscalatedAt { get; set; }

    // When the incident reached RESOLVED. Set automatically by the status
    // endpoint, and cleared if the incident is reopened - so the pair
    // (CreatedAt, ResolvedAt) always describes the CURRENT resolution, not a
    // stale one from a previous time someone closed it too early.
    public DateTime? ResolvedAt { get; set; }

    // Stamped by the background SLA monitor the first time this incident is
    // found past its acknowledgement deadline (see
    // Realtime/SlaMonitorBackgroundService.cs). Storing the moment it
    // happened - rather than recomputing "is it late?" on every read - means
    // the breach stays in the record even after somebody acknowledges it,
    // which is what you want when reviewing how the week went.
    public DateTime? SlaBreachedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    // Navigation properties: EF Core uses these to run the right JOIN/SELECT
    // when we ask for an incident "with its tasks", instead of us writing SQL
    // by hand. Configured with cascade delete in AppDbContext, so deleting an
    // incident also deletes its tasks/logs/participants - no orphaned rows.
    public List<TaskItem> Tasks { get; set; } = [];

    public List<IncidentParticipant> Participants { get; set; } = [];

    public List<ActivityLog> ActivityLogs { get; set; } = [];

    // What people actually said to each other while working on this - as
    // opposed to ActivityLogs, which is what the app recorded. See
    // Entities/Comment.cs for why those are kept apart.
    public List<Comment> Comments { get; set; } = [];

    // The owning team. Unlike CreatedBy (which is only ever an id), we do
    // want to read `incident.Department.Name` when showing the incident, so
    // this one gets a real navigation property.
    public Department Department { get; set; } = null!;

    // The Incident Commander as a full user object, for showing their name
    // and job title on the page. Null whenever CommanderUserId is null.
    public User? Commander { get; set; }
}
