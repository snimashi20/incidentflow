// These three enums describe the fixed sets of values a few columns can hold.
// We store them in the database as plain text (e.g. "HIGH", not the number 2)
// so a quick look at the table with any database tool is still readable -
// see AppDbContext.cs where we tell EF Core "store these as strings".
namespace IncidentFlow.Api.Entities;

// How serious an incident is.
public enum Severity
{
    LOW,
    MEDIUM,
    HIGH,
    CRITICAL,
}

// The lifecycle of an incident: it starts OPEN, someone marks it
// INVESTIGATING while working on it, then RESOLVED once fixed.
public enum IncidentStatus
{
    OPEN,
    INVESTIGATING,
    RESOLVED,
}

// The lifecycle of a single task within an incident.
//
// Named "IncidentTaskStatus" instead of just "TaskStatus" on purpose: .NET's
// own System.Threading.Tasks namespace already defines a type called
// TaskStatus, and every async method in this project returns a
// System.Threading.Tasks.Task. Reusing that name for our own enum would make
// the compiler (and anyone reading the code) unsure which "TaskStatus" is
// meant, so we pick a name that can never collide.
public enum IncidentTaskStatus
{
    TODO,
    IN_PROGRESS,
    DONE,
}
