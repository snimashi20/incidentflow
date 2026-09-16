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

// What the client sends us when registering a new account.
public record UserCreate(string Email, string Name, string Password);

// What the client sends us when logging in.
public record UserLogin(string Email, string Password);

// What we send back to describe a user - notice there's no password field
// here at all, so it's impossible to accidentally leak a password hash.
public record UserOut(int Id, string Email, string Name);

// The JWT access token we return after a successful login.
public record TokenResponse(string AccessToken, string TokenType = "bearer");
