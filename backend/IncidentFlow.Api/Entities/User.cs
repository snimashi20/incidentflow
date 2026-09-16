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

    public DateTime CreatedAt { get; set; }
}
