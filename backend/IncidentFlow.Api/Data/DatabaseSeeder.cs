// Puts the rows from SeedData.cs into the database when the app starts up
// (called from Program.cs, right after EnsureCreatedAsync creates the tables).
//
// The one rule this file follows everywhere: seeding must be IDEMPOTENT -
// running it ten times must leave the database in exactly the same state as
// running it once. The backend restarts constantly during development
// (`dotnet watch` reloads on every file save), so a seeder that blindly
// inserted rows would give you 500 duplicate Marcus Webbs by lunchtime.
// That is why every insert below is guarded by "is this already there?".
using IncidentFlow.Api.Entities;
using IncidentFlow.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext db, bool includeDemoEmployees, ILogger logger)
    {
        // Departments are reference data: the app genuinely needs them to
        // work (you cannot register without picking one), so they are
        // always seeded, even when the demo employees are switched off.
        var departmentsByCode = await SeedDepartmentsAsync(db, logger);

        if (!includeDemoEmployees)
        {
            logger.LogInformation("Demo employee seeding is disabled (Seed:DemoEmployees=false) - skipping.");
            return;
        }

        await SeedEmployeesAsync(db, departmentsByCode, logger);
    }

    // Inserts any department from SeedData that is not in the database yet,
    // and returns a Code -> Id lookup covering all of them. Existing rows
    // are left exactly as they are (we never overwrite), so renaming a
    // department by hand in the database survives a restart.
    private static async Task<Dictionary<string, int>> SeedDepartmentsAsync(AppDbContext db, ILogger logger)
    {
        var existing = await db.Departments.ToDictionaryAsync(d => d.Code, d => d);

        var added = 0;
        foreach (var seed in SeedData.Departments)
        {
            if (existing.ContainsKey(seed.Code)) continue;

            var department = new Department
            {
                Code = seed.Code,
                Name = seed.Name,
                Kind = seed.Kind,
                Description = seed.Description,
            };
            db.Departments.Add(department);
            existing[seed.Code] = department;
            added++;
        }

        if (added > 0)
        {
            // SaveChanges is what actually sends the INSERTs to Postgres,
            // and it is also what fills in each new department's Id.
            await db.SaveChangesAsync();
            logger.LogInformation("Seeded {Count} departments.", added);
        }

        return existing.ToDictionary(pair => pair.Key, pair => pair.Value.Id);
    }

    private static async Task SeedEmployeesAsync(
        AppDbContext db, Dictionary<string, int> departmentsByCode, ILogger logger
    )
    {
        // One database round-trip to find out which of our 75 demo emails
        // already exist, instead of 75 separate "does this user exist?"
        // queries. HashSet gives fast "is this email in the list?" checks.
        var seedEmails = SeedData.Employees.Select(e => e.Email).ToList();
        var existingEmails = (await db.Users
            .Where(u => seedEmails.Contains(u.Email))
            .Select(u => u.Email)
            .ToListAsync()).ToHashSet();

        // Hashing a password with bcrypt is deliberately slow (that is the
        // whole point - it makes guessing passwords expensive). Doing it 50
        // times would add real seconds to every startup, and all the demo
        // accounts share one password anyway, so we hash it once and reuse
        // the result. Never do this for real users: each real password must
        // get its own hash, with its own random salt.
        var demoHash = PasswordHasher.Hash(SeedData.DemoPassword);

        var added = 0;
        foreach (var seed in SeedData.Employees)
        {
            if (existingEmails.Contains(seed.Email)) continue;

            // A typo in a department code in SeedData.cs would otherwise
            // show up much later as a confusing foreign-key error, so we
            // fail loudly and immediately instead.
            if (!departmentsByCode.TryGetValue(seed.DepartmentCode, out var departmentId))
            {
                throw new InvalidOperationException(
                    $"Seed employee '{seed.Email}' refers to unknown department code '{seed.DepartmentCode}'."
                );
            }

            db.Users.Add(new User
            {
                Name = seed.Name,
                Email = seed.Email,
                HashedPassword = demoHash,
                JobTitle = seed.JobTitle,
                Seniority = seed.Seniority,
                EmploymentType = seed.EmploymentType,
                DepartmentId = departmentId,
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            logger.LogInformation(
                "Seeded {Count} demo employees (password for all of them: {Password}).",
                added,
                SeedData.DemoPassword
            );
        }
    }
}
