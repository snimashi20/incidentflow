namespace IncidentFlow.Api.Entities;

// One message someone typed into an incident's discussion.
//
// This is deliberately a separate table from ActivityLog, even though the
// two look similar on screen. The difference is who writes them: the app
// writes activity ("Dana changed status OPEN -> INVESTIGATING"), a person
// writes a comment ("rolling back the deploy now, give me 5 minutes").
// Mixing them would mean you could never show one without the other, and
// a postmortem that replays only what the system did - or only what people
// said - is exactly the kind of thing you want to be able to ask for.
public class Comment
{
    public int Id { get; set; }

    public int IncidentId { get; set; }

    // Who wrote it. Like the rest of the app, a comment always belongs to a
    // real user - there is no anonymous posting.
    public int UserId { get; set; }

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // Navigation properties - see Entities/Incident.cs for what these are.
    // Deleting an incident deletes its comments (cascade); deleting a user
    // is refused while they still have comments (restrict), so the history
    // never ends up with messages from nobody.
    public Incident Incident { get; set; } = null!;

    public User User { get; set; } = null!;
}
