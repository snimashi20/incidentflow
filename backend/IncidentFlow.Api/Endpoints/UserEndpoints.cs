// The employee directory. Mainly used to populate the "assign task to..."
// dropdown on the frontend, which needs the list of people to choose from -
// now with each person's department and job title, so whoever is running an
// incident can tell a Database Reliability Engineer from an accountant.
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Dtos;
using IncidentFlow.Api.Entities;
using IncidentFlow.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        // The optional departmentId parameter is read from the query string:
        // /api/users?department_id=3 narrows the list to that one
        // department, while plain /api/users still returns everybody.
        // [FromQuery(Name = ...)] spells out the snake_case name the rest of
        // this API uses - query strings are not JSON, so the snake_case
        // convention set up in Program.cs does not apply to them
        // automatically.
        app.MapGet("/api/users", async (
            HttpContext context,
            AppDbContext db,
            JwtService jwt,
            [FromQuery(Name = "department_id")] int? departmentId,
            [FromQuery(Name = "min_seniority")] string? minSeniority
        ) =>
        {
            // Still requires login even though we don't use the current user
            // directly - this keeps the user list from being readable by
            // anyone who isn't authenticated.
            await CurrentUser.RequireAsync(context, db, jwt);

            // .Include pulls each user's department in the same query, so
            // building the response below doesn't fire one extra SELECT per
            // user (the classic "N+1 queries" performance trap).
            var query = db.Users.Include(u => u.Department).AsQueryable();

            if (departmentId is not null)
            {
                query = query.Where(u => u.DepartmentId == departmentId);
            }

            var users = await query.OrderBy(u => u.Name).ToListAsync();

            // "Who in this department is senior enough to escalate to?" -
            // /api/users?department_id=3&min_seniority=LEAD returns the
            // tech leads, managers, directors and VPs, and nobody below.
            //
            // This filter runs in C#, AFTER the database query, on purpose:
            // the column stores the level as text ("LEAD"), so asking
            // Postgres for ">= LEAD" would compare strings alphabetically
            // and give nonsense. Comparing the enum values in C# ranks them
            // properly, by their declaration order in Enums.cs. Fine at
            // this scale (one company's employees); a system with millions
            // of users would store the rank as a number and filter in SQL.
            if (!string.IsNullOrWhiteSpace(minSeniority))
            {
                if (!Enum.TryParse<Seniority>(minSeniority, ignoreCase: true, out var floor))
                {
                    throw ApiException.UnprocessableEntity(
                        $"Unknown seniority level. Expected one of: {string.Join(", ", Enum.GetNames<Seniority>())}"
                    );
                }

                // Most senior first once the filter is on: if you asked this
                // question you are looking for someone to escalate to, and
                // the most senior person is the answer you want at the top.
                // (Without the filter the list stays alphabetical - it's the
                // employee directory then, not an escalation list.)
                users = [.. users
                    .Where(u => u.Seniority >= floor)
                    .OrderByDescending(u => u.Seniority)
                    .ThenBy(u => u.Name)];
            }

            return Results.Ok(users.Select(UserOut.From));
        }).WithTags("users");
    }
}
