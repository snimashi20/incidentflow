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

// The three broad groups a department falls into. Used purely for grouping
// in the UI (so a dropdown can show "Product areas" and "Corporate
// functions" as separate sections) - it has no effect on permissions.
public enum DepartmentKind
{
    // Builds and runs the actual products: Search, Ads, Cloud, Android...
    PRODUCT_AREA,

    // Central company functions: Legal, Finance, HR, Marketing...
    CORPORATE_FUNCTION,

    // The people who keep the campus itself running: catering, facilities,
    // shuttles, on-site health.
    CAMPUS_OPERATIONS,
}

// How senior someone is - the career ladder, from a brand new hire up to
// the executive team.
//
// The ORDER of these members matters, unlike the other enums in this file.
// C# numbers them from the top down (JUNIOR = 0 ... EXECUTIVE = 8), which
// makes "at this level or above" a simple comparison:
//     user.Seniority >= Seniority.LEAD
// That is exactly the question an incident asks when it needs escalating,
// so always ADD new levels in rank order rather than tacking them on the
// end. (The column still stores the text name - see AppDbContext.cs - so
// the database stays readable; the numbers are only used for comparisons
// in C#.)
public enum Seniority
{
    // Individual contributors, in increasing order of experience. STAFF
    // sits above SENIOR, following the usual big-tech ladder (senior ->
    // staff -> principal).
    JUNIOR,
    MID,
    SENIOR,
    STAFF,

    // Tech leads: still hands-on engineers, but they own the technical
    // direction of a system and are who you want on a serious outage.
    LEAD,

    // People managers and above - "senior management" in everyday speech.
    MANAGER,
    DIRECTOR,
    VP,

    // C-suite: CTO, CFO and friends. The end of the escalation chain.
    EXECUTIVE,
}

// How someone is employed. Google's own shorthand: FTEs are full-time
// employees, TVCs are Temps, Vendors and Contractors. Worth modelling
// because it changes who you can reasonably put on an incident.
public enum EmploymentType
{
    FTE,
    TVC,
}
