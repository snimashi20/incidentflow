using IncidentFlow.Api.Common;
using IncidentFlow.Api.Entities;

namespace IncidentFlow.Api.Dtos;

// What the client sends to open a new incident. DepartmentId is required for
// the same reason it is required when registering a user: an incident that
// belongs to no team cannot be escalated to anyone (see Entities/Incident.cs).
public record IncidentCreate(string Title, Severity Severity, int DepartmentId, string Description = "");

public record IncidentStatusUpdate(IncidentStatus Status);

// Sent to PATCH /api/incidents/{id}/commander. UserId is nullable on purpose:
// sending null is how you hand the role back / clear it.
public record IncidentCommanderUpdate(int? UserId);

// A short summary of an incident, used for the incidents list page.
public record IncidentOut(
    int Id,
    string Title,
    string Description,
    Severity Severity,
    IncidentStatus Status,
    int CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int DepartmentId,
    string? DepartmentName,
    int? CommanderUserId,
    DateTime? AcknowledgedAt,
    DateTime? EscalatedAt,
    DateTime? ResolvedAt,
    // When this incident has to be acknowledged by. Computed here from the
    // severity (see Common/SlaPolicy.cs) and sent to the frontend as a plain
    // timestamp, so the countdown on screen doesn't need its own copy of the
    // SLA table - one source of truth, on the server.
    DateTime SlaDeadline,
    DateTime? SlaBreachedAt
)
{
    // DepartmentName is only filled in when the caller remembered to
    // .Include(i => i.Department). The `?` guards against a null department
    // rather than throwing, since a missing label is a cosmetic problem and
    // crashing the whole list over it would not be.
    public static IncidentOut From(Incident incident) => new(
        incident.Id, incident.Title, incident.Description, incident.Severity, incident.Status,
        incident.CreatedBy, incident.CreatedAt, incident.UpdatedAt,
        incident.DepartmentId, incident.Department?.Name,
        incident.CommanderUserId,
        incident.AcknowledgedAt, incident.EscalatedAt, incident.ResolvedAt,
        SlaPolicy.AcknowledgeDeadline(incident), incident.SlaBreachedAt
    );
}

// The full incident, including its tasks, comments, activity feed, and who
// has joined it. This is what powers the incident detail page and is also
// what gets broadcast over the WebSocket whenever the incident changes.
public record IncidentDetail(
    int Id,
    string Title,
    string Description,
    Severity Severity,
    IncidentStatus Status,
    int CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int DepartmentId,
    string? DepartmentName,
    int? CommanderUserId,
    DateTime? AcknowledgedAt,
    DateTime? EscalatedAt,
    DateTime? ResolvedAt,
    DateTime SlaDeadline,
    DateTime? SlaBreachedAt,
    List<TaskOut> Tasks,
    List<ActivityOut> ActivityLogs,
    List<UserOut> Participants,
    List<CommentOut> Comments,
    // The commander as a whole user, so the page can show their name and job
    // title without looking them up in a separate list.
    UserOut? Commander
);
