namespace IncidentFlow.Api.Entities;

// One row per registered account. This is an "entity" - a plain C# class that
// Entity Framework Core (EF Core, our ORM - Object Relational Mapper) maps
// onto a database table for us.
public class User
{
    public int Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    // We never store the password a user actually typed - only its bcrypt
    // hash (see Security/PasswordHasher.cs).
    public string HashedPassword { get; set; } = string.Empty;

    // Which department this person works in. Every employee belongs to
    // exactly one. The column is a plain (non-nullable) int, so the database
    // itself refuses to store a user with no department at all.
    public int DepartmentId { get; set; }

    // Their role inside that department, e.g. "Site Reliability Engineer".
    // Free text rather than an enum: real job titles change constantly, and
    // we only ever display this string - no code branches on it.
    public string JobTitle { get; set; } = string.Empty;

    // Where they sit on the career ladder, from JUNIOR up to EXECUTIVE.
    // Unlike JobTitle above, this one IS a real enum, because code needs to
    // compare it: "find me someone at LEAD or above in this department to
    // escalate this outage to". MID is the default for a new account, since
    // it is the least surprising thing to assume about a stranger.
    public Seniority Seniority { get; set; } = Seniority.MID;

    // Full-time employee vs. temp/vendor/contractor - see Enums.cs.
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FTE;

    public DateTime CreatedAt { get; set; }

    // Navigation property. `= null!` tells the compiler "trust me, this is
    // never actually null" - EF Core fills it in whenever we load a user
    // with .Include(u => u.Department), and the non-nullable DepartmentId
    // above guarantees there is always a department row to fill it with.
    public Department Department { get; set; } = null!;
}
