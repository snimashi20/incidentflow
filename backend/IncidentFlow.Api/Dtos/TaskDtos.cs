using IncidentFlow.Api.Entities;

namespace IncidentFlow.Api.Dtos;

public record TaskCreate(string Title, int? AssignedTo = null);

// Every field here is optional (nullable) because a PATCH request only needs
// to send the fields it wants to change (e.g. just {"status": "DONE"}).
public record TaskUpdate(string? Title = null, IncidentTaskStatus? Status = null, int? AssignedTo = null);

public record TaskOut(
    int Id,
    int IncidentId,
    string Title,
    IncidentTaskStatus Status,
    int? AssignedTo,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static TaskOut From(TaskItem task) => new(
        task.Id, task.IncidentId, task.Title, task.Status, task.AssignedTo, task.CreatedAt, task.UpdatedAt
    );
}
