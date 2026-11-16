// "DTOs" (Data Transfer Objects) describe the *shape* of data going in and
// out of our API - separate from the EF Core entities in Entities/, which
// describe the database tables. Keeping them separate means we control
// exactly what a client can send us (e.g. they can't set `Id` or
// `HashedPassword` themselves) and exactly what we send back (e.g. never the
// password hash).
//
// These are C# "records" - a compact way to declare an immutable data-holder
// class. `record Foo(string Bar)` gives you a class with a `Bar` property,
// a constructor, and sensible equality, all in one line.
namespace IncidentFlow.Api.Dtos;

// What the client sends us when registering a new account. DepartmentId is
// required: every employee belongs to exactly one department, so there is
// no sensible default we could pick on their behalf (guessing would put a
// new accountant in the Cloud on-call rotation).
// Seniority is optional text ("SENIOR", "DIRECTOR", ...) and defaults to
// MID when it isn't given - see AuthEndpoints.cs, which rejects anything
// that isn't one of the levels in the Seniority enum.
public record UserCreate(
    string Email,
    string Name,
    string Password,
    int DepartmentId,
    string? JobTitle = null,
    string? Seniority = null
);

// What the client sends us when logging in.
public record UserLogin(string Email, string Password);

// What we send back to describe a user - notice there's no password field
// here at all, so it's impossible to accidentally leak a password hash.
//
// JobTitle/Department are optional (`= null`) so the places that only ever
// need a name - activity log entries, for instance - can keep building a
// UserOut from three values without loading the department from the
// database just to throw it away.
public record UserOut(
    int Id,
    string Email,
    string Name,
    string? JobTitle = null,
    string? Seniority = null,
    string? EmploymentType = null,
    DepartmentOut? Department = null
)
{
    // Builds the full version, including department. The user passed in
    // must have been loaded with .Include(u => u.Department), otherwise
    // user.Department would be null at runtime.
    public static UserOut From(Entities.User user) => new(
        user.Id,
        user.Email,
        user.Name,
        user.JobTitle,
        user.Seniority.ToString(),
        user.EmploymentType.ToString(),
        DepartmentOut.From(user.Department)
    );
}

// The JWT access token we return after a successful login.
public record TokenResponse(string AccessToken, string TokenType = "bearer");

// A short-lived credential for opening a WebSocket, handed out by
// POST /api/auth/ws-ticket. ExpiresInSeconds is included so the frontend
// never has to hard-code a lifetime that the server might change.
public record WebSocketTicketResponse(string Ticket, int ExpiresInSeconds);
