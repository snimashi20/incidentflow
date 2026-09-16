namespace IncidentFlow.Api.Security;

// bcrypt is a slow-by-design hashing algorithm - that's a feature, not a bug,
// because it makes brute-forcing stolen password hashes much harder. This
// wraps the BCrypt.Net-Next library.
public static class PasswordHasher
{
    // Turns a plain-text password into an irreversible hash to store in the database.
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    // Checks a login attempt's password against the stored hash. We can't
    // "decrypt" the hash back into the original password - we can only
    // re-hash the attempt and compare the two hashes.
    public static bool Verify(string password, string hashedPassword) =>
        BCrypt.Net.BCrypt.Verify(password, hashedPassword);
}
