namespace IncidentFlow.Api.Dtos;

// One entry in the incident's "Team Activity" feed.
public record ActivityOut(int Id, string Message, DateTime CreatedAt, UserOut User);
