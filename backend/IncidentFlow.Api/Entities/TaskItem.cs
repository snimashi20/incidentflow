namespace IncidentFlow.Api.Entities;

// Called "TaskItem" rather than "Task" for the same reason the status enum
// above is called "IncidentTaskStatus": System.Threading.Tasks.Task is
// already a fundamental .NET type (the return type of nearly every async
// method here), so reusing that name for our own entity would be confusing
// at best and a compiler error at worst.
public class TaskItem
{
    public int Id { get; set; }

    public int IncidentId { get; set; }

    public string Title { get; set; } = string.Empty;

    public IncidentTaskStatus Status { get; set; } = IncidentTaskStatus.TODO;

    // Nullable because a task can exist unassigned before someone picks it up.
    public int? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Incident Incident { get; set; } = null!;

    public User? Assignee { get; set; }
}
