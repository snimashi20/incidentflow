namespace IncidentFlow.Api.Entities;

// One row per department / org unit of the company (e.g. "Google Cloud",
// "Finance & Corporate Development").
//
// Why this table exists: an incident is almost always owned by ONE part of
// the company. If the production database falls over, that is a job for the
// backend engineers in Google Cloud / Core Systems - not for the accountants
// in Finance. Giving every user a department is the first step towards
// "only show me the people who could actually fix this".
public class Department
{
    public int Id { get; set; }

    // The human-readable name shown in the UI, e.g. "Google Cloud".
    public string Name { get; set; } = string.Empty;

    // A short, stable, unique code, e.g. "CLOUD". Names get rewritten by
    // marketing every few years; codes don't, so the seed data and any
    // future config refer to departments by code rather than by name.
    public string Code { get; set; } = string.Empty;

    // Which of the three broad groups this department belongs to - see
    // DepartmentKind in Enums.cs.
    public DepartmentKind Kind { get; set; }

    // One line explaining what the department actually does. Handy as a
    // tooltip in the UI and as documentation for whoever reads the seed data.
    public string Description { get; set; } = string.Empty;

    // Navigation property: every user whose DepartmentId points at this row.
    // EF Core fills this in for us when we ask for a department "with its
    // members" - we never have to write the JOIN by hand.
    public List<User> Members { get; set; } = [];
}
