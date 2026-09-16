using IncidentFlow.Api.Data;
using IncidentFlow.Api.Entities;
using IncidentFlow.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Common;

// Every endpoint that needs a logged-in user calls one of the two static
// methods below explicitly, right at the top of the handler: read the JWT
// from the request, then look up the matching user.
public static class CurrentUser
{
    public static Task<User?> FindByEmailAsync(AppDbContext db, string email) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email);

    // Used by every protected HTTP route. Reads the "Authorization: Bearer
    // <token>" header, decodes it, and looks up the matching user - or throws
    // a 401 Unauthorized if anything along the way fails.
    public static async Task<User> RequireAsync(HttpContext context, AppDbContext db, JwtService jwt)
    {
        var header = context.Request.Headers.Authorization.ToString();
        string? token = null;
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            token = header["Bearer ".Length..];
        }

        var email = jwt.ValidateAndGetEmail(token);
        if (email is null) throw ApiException.Unauthorized();

        var user = await FindByEmailAsync(db, email);
        if (user is null) throw ApiException.Unauthorized();

        return user;
    }

    // Same idea, but for WebSocket connections. Browsers can't attach custom
    // headers during the WebSocket handshake, so the frontend passes the
    // token as a query string parameter instead (see Program.cs's WebSocket
    // route). Returns null instead of throwing so the caller can decide how
    // to close the connection.
    public static async Task<User?> FromWebSocketQueryAsync(HttpContext context, AppDbContext db, JwtService jwt)
    {
        var token = context.Request.Query["token"].ToString();
        var email = jwt.ValidateAndGetEmail(token);
        if (email is null) return null;
        return await FindByEmailAsync(db, email);
    }
}
