namespace IncidentFlow.Api.Entities;

// One row per event in an incident's "Team Activity" feed - e.g. "Sithmi
// joined the incident" or "Sithmi changed status OPEN -> INVESTIGATING".
public class ActivityLog
{
    public int Id { get; set; }

    public int IncidentId { get; set; }

    public int UserId { get; set; }

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public Incident Incident { get; set; } = null!;

    public User User { get; set; } = null!;
}
