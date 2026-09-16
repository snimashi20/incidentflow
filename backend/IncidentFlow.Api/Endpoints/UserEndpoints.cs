// A small endpoint used only to populate the "assign task to..." dropdown on
// the frontend - it needs the list of registered users to choose from.
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Dtos;
using IncidentFlow.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/users", async (HttpContext context, AppDbContext db, JwtService jwt) =>
        {
            // Still requires login even though we don't use the current user
            // directly - this keeps the user list from being readable by
            // anyone who isn't authenticated.
            await CurrentUser.RequireAsync(context, db, jwt);

            var users = await db.Users.OrderBy(u => u.Name).ToListAsync();
            return Results.Ok(users.Select(u => new UserOut(u.Id, u.Email, u.Name)));
        }).WithTags("users");
    }
}
