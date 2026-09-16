namespace IncidentFlow.Api.Entities;

// Join table: which users have joined which incident. A user can only join a
// given incident once (enforced in the endpoint code, not the database).
public class IncidentParticipant
{
    public int Id { get; set; }

    public int IncidentId { get; set; }

    public int UserId { get; set; }

    public DateTime JoinedAt { get; set; }

    public Incident Incident { get; set; } = null!;

    public User User { get; set; } = null!;
}
