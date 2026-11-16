// Tests for the company seeded into an empty database: the 14 departments
// and the 75 demo employees in IncidentFlow.Api/Data/SeedData.cs.
//
// This is the one test class that asks for the demo employees (every other
// class turns them off), which is what the SeedDemoEmployees override below
// does - see IntegrationTestBase.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class SeedDataTests : IntegrationTestBase
{
    protected override bool SeedDemoEmployees => true;

    [Fact]
    public async Task Departments_AreSeeded_AndReadableWithoutLoggingIn()
    {
        // No Authorization header: the registration form needs this list
        // before anyone has an account, so it must work logged out.
        var response = await Client.GetAsync("/api/departments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var departments = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(SeedData.Departments.Length, departments.GetArrayLength());
    }

    [Fact]
    public async Task EveryEmployee_IsSeeded_InExactlyOneDepartment_WithALevel()
    {
        var client = await AuthenticatedClientAsync("seed-reader@example.com");

        var users = await client.GetFromJsonAsync<JsonElement>("/api/users", Json);

        // Every seeded employee, plus the one account this test registered.
        Assert.Equal(SeedData.Employees.Length + 1, users.GetArrayLength());

        foreach (var user in users.EnumerateArray())
        {
            // The whole point: nobody is floating around without a
            // department, and nobody has two.
            var department = user.GetProperty("department");
            Assert.NotEqual(JsonValueKind.Null, department.ValueKind);
            Assert.False(string.IsNullOrWhiteSpace(department.GetProperty("name").GetString()));

            // ...and everybody sits somewhere on the career ladder.
            var seniority = user.GetProperty("seniority").GetString();
            Assert.True(Enum.TryParse<Seniority>(seniority, out _), $"Unexpected seniority '{seniority}'");
        }
    }

    [Fact]
    public void EveryDepartment_HasSomeoneSeniorToEscalateTo()
    {
        // A department full of mid-level engineers has nobody to hand a
        // serious incident to, so each one needs at least one person at
        // LEAD or above (tech lead, manager, director, VP or C-suite).
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // ToList() first, then compare: the Seniority column stores text, so
        // a ">= LEAD" written inside the EF query would be sent to the
        // database as an alphabetical string comparison and quietly give the
        // wrong answer. Ranking only works on the enum, in C#.
        var employees = db.Users.ToList();

        foreach (var department in db.Departments.ToList())
        {
            var seniorPeople = employees.Count(
                u => u.DepartmentId == department.Id && u.Seniority >= Seniority.LEAD
            );

            Assert.True(seniorPeople > 0, $"{department.Name} has nobody at LEAD level or above");
        }
    }

    [Fact]
    public async Task Users_CanBeFilteredToSeniorPeopleOnly()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cloud = db.Departments.Single(d => d.Code == "CLOUD");

        var client = await AuthenticatedClientAsync("escalation-reader@example.com");

        // "Who in Cloud can I escalate this outage to?"
        var seniors = await client.GetFromJsonAsync<JsonElement>(
            $"/api/users?department_id={cloud.Id}&min_seniority=LEAD", Json
        );

        var expected = SeedData.Employees.Count(e => e.DepartmentCode == "CLOUD" && e.Seniority >= Seniority.LEAD);
        Assert.Equal(expected, seniors.GetArrayLength());
        Assert.True(expected > 0);

        // Nobody below the requested level sneaks in - note this would fail
        // if the filter compared the stored TEXT instead of the enum's rank,
        // since "MID" sorts after "LEAD" alphabetically.
        foreach (var user in seniors.EnumerateArray())
        {
            var level = Enum.Parse<Seniority>(user.GetProperty("seniority").GetString()!);
            Assert.True(level >= Seniority.LEAD, $"{user.GetProperty("name")} is only {level}");
        }
    }

    [Fact]
    public async Task Users_RejectsAnUnknownSeniorityFilter()
    {
        var client = await AuthenticatedClientAsync("bad-filter@example.com");

        var response = await client.GetAsync("/api/users?min_seniority=SUPREME_LEADER");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task DepartmentMembers_AreListedMostSeniorFirst()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cloud = db.Departments.Single(d => d.Code == "CLOUD");

        var client = await AuthenticatedClientAsync("roster-reader@example.com");
        var members = await client.GetFromJsonAsync<JsonElement>($"/api/departments/{cloud.Id}/members", Json);

        // Walk the list and check each person is no more senior than the one
        // before them - i.e. the ranking really is descending.
        var previous = Seniority.EXECUTIVE;
        foreach (var member in members.EnumerateArray())
        {
            var level = Enum.Parse<Seniority>(member.GetProperty("seniority").GetString()!);
            Assert.True(level <= previous, "Department members came back out of seniority order");
            previous = level;
        }
    }

    [Fact]
    public async Task Users_CanBeFilteredToOneDepartment()
    {
        // This is the "a database failure is Cloud's problem, not Finance's"
        // query: ask for the members of one department and get only them.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cloud = db.Departments.Single(d => d.Code == "CLOUD");

        var client = await AuthenticatedClientAsync("filter-reader@example.com");
        var users = await client.GetFromJsonAsync<JsonElement>($"/api/users?department_id={cloud.Id}", Json);

        var expected = SeedData.Employees.Count(e => e.DepartmentCode == "CLOUD");
        Assert.Equal(expected, users.GetArrayLength());

        // And nobody from Finance crept into the list.
        foreach (var user in users.EnumerateArray())
        {
            Assert.Equal("CLOUD", user.GetProperty("department").GetProperty("code").GetString());
        }
    }

    [Fact]
    public async Task Seeding_IsIdempotent_SoRestartsDoNotDuplicatePeople()
    {
        // The backend re-runs the seeder on every startup (and `dotnet
        // watch` restarts it constantly), so running it twice against the
        // same database must not create a second copy of everyone.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("SeedTest");

        var usersBefore = db.Users.Count();
        var departmentsBefore = db.Departments.Count();

        await DatabaseSeeder.SeedAsync(db, includeDemoEmployees: true, logger);

        Assert.Equal(usersBefore, db.Users.Count());
        Assert.Equal(departmentsBefore, db.Departments.Count());
    }
}
