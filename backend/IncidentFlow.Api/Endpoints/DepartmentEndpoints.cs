// Read-only endpoints for the company's departments.
//
// Unlike nearly everything else in this API, GET /api/departments does NOT
// require a login - the registration form has to show the list of
// departments to someone who, by definition, does not have an account yet.
// That is safe here because the list is just org-chart reference data: no
// personal information, nothing an attacker could use.
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Dtos;
using IncidentFlow.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Endpoints;

public static class DepartmentEndpoints
{
    public static void MapDepartmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/departments").WithTags("departments");

        group.MapGet("/", async (AppDbContext db) =>
        {
            // Counting members in the same query (rather than loading every
            // user and counting in C#) keeps this to a single SELECT with a
            // COUNT - EF Core translates the Select below into SQL for us.
            var departments = await db.Departments
                .OrderBy(d => d.Kind)
                .ThenBy(d => d.Name)
                .Select(d => new { Department = d, MemberCount = d.Members.Count })
                .ToListAsync();

            return Results.Ok(departments.Select(row => DepartmentOut.From(row.Department, row.MemberCount)));
        });

        // Everyone in one department - this is the "who could actually work
        // on this incident?" query. A database outage is a job for the
        // engineers in CLOUD or CORE; Finance never needs to appear in that
        // list at all.
        group.MapGet("/{id:int}/members", async (int id, HttpContext context, AppDbContext db, JwtService jwt) =>
        {
            // Actual people, so this one does require a login.
            await CurrentUser.RequireAsync(context, db, jwt);

            var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id)
                ?? throw ApiException.NotFound("Department not found");

            var members = await db.Users
                .Include(u => u.Department)
                .Where(u => u.DepartmentId == department.Id)
                .OrderBy(u => u.Name)
                .ToListAsync();

            // Most senior first (VP before tech lead before engineer), then
            // alphabetically within a level - so the person you would
            // escalate to is at the top of the list rather than wherever
            // the alphabet happens to put them. Sorted here in C# rather
            // than in SQL for the reason explained in UserEndpoints.cs: the
            // level is stored as text, so only C# can rank it correctly.
            var ranked = members
                .OrderByDescending(u => u.Seniority)
                .ThenBy(u => u.Name);

            return Results.Ok(ranked.Select(UserOut.From));
        });
    }
}
