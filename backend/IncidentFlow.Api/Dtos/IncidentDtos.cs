using IncidentFlow.Api.Entities;

namespace IncidentFlow.Api.Dtos;

public record IncidentCreate(string Title, Severity Severity, string Description = "");

public record IncidentStatusUpdate(IncidentStatus Status);

// A short summary of an incident, used for the incidents list page.
public record IncidentOut(
    int Id,
    string Title,
    string Description,
    Severity Severity,
    IncidentStatus Status,
    int CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static IncidentOut From(Incident incident) => new(
        incident.Id, incident.Title, incident.Description, incident.Severity, incident.Status,
        incident.CreatedBy, incident.CreatedAt, incident.UpdatedAt
    );
}

// The full incident, including its tasks, activity feed, and who has joined
// it. This is what powers the incident detail page and is also what gets
// broadcast over the WebSocket whenever the incident changes.
public record IncidentDetail(
    int Id,
    string Title,
    string Description,
    Severity Severity,
    IncidentStatus Status,
    int CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<TaskOut> Tasks,
    List<ActivityOut> ActivityLogs,
    List<UserOut> Participants
);
