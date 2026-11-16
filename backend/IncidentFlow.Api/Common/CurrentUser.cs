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

    // Same idea, but for WebSocket connections.
    //
    // Browsers can't attach custom headers during a WebSocket handshake, so
    // something has to travel in the URL. That something is a ticket, NOT
    // the access token: a URL ends up in access logs, proxy logs and browser
    // history, and a day-long token has no business being in any of them.
    // The ticket is random, expires in 30 seconds and works exactly once -
    // see Security/WebSocketTicketStore.cs.
    //
    // Returns null instead of throwing so the caller can decide how to close
    // the connection.
    public static async Task<User?> FromWebSocketTicketAsync(
        HttpContext context, AppDbContext db, IWebSocketTicketStore tickets
    )
    {
        var ticket = context.Request.Query["ticket"].ToString();

        // Redeeming destroys the ticket, so this is also what stops the same
        // URL being replayed from a log file.
        var email = await tickets.RedeemAsync(ticket);
        if (email is null) return null;

        return await FindByEmailAsync(db, email);
    }
}
