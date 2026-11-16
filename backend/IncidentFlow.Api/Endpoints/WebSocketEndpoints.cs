// The actual WebSocket endpoint the frontend connects to
// (ws://.../ws/incidents/{incidentId}). Unlike a normal HTTP route, a
// WebSocket connection stays open for as long as the client is connected -
// this handler doesn't return until the browser disconnects.
using System.Net.WebSockets;
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Realtime;
using IncidentFlow.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Endpoints;

public static class WebSocketEndpoints
{
    public static void MapIncidentWebSocket(this IEndpointRouteBuilder app)
    {
        app.MapGet("/ws/incidents/{incidentId:int}", async (
            HttpContext context, int incidentId, AppDbContext db, IWebSocketTicketStore tickets,
            IncidentSocketManager sockets
        ) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // Browsers can't attach custom headers (like our usual
            // "Authorization: Bearer <token>") to a WebSocket handshake, so
            // something has to travel in the URL - and whatever does will end
            // up in access logs, proxy logs and browser history.
            //
            // So it is a ticket, not the access token: random, valid for 30
            // seconds, and destroyed the moment it is used. The client gets
            // one from POST /api/auth/ws-ticket, which is an ordinary request
            // carrying the real token in a header where it belongs. See
            // Security/WebSocketTicketStore.cs.
            var user = await CurrentUser.FromWebSocketTicketAsync(context, db, tickets);
            if (user is null)
            {
                // Reject the handshake itself (never call AcceptWebSocketAsync)
                // rather than accepting the connection and immediately
                // closing it - this way the browser/test client sees the
                // connection attempt fail outright, matching "unauthorized"
                // as directly as a WebSocket handshake allows.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Knowing who you are is not the same as being allowed to watch
            // this particular incident. Without this check, any account could
            // subscribe to any incident id and receive its live feed - and now
            // that the socket also carries presence, it would additionally
            // reveal that person's name to everyone legitimately on the call.
            // Same rule as the REST endpoints, from Common/IncidentAccess.cs.
            var incident = await db.Incidents
                .Include(i => i.Participants)
                .Include(i => i.Tasks)
                .FirstOrDefaultAsync(i => i.Id == incidentId);

            if (incident is null || !IncidentAccess.CanSee(incident, user))
            {
                // Deliberately the same answer in both cases: telling a
                // stranger apart "no such incident" from "it exists but it is
                // not yours" leaks which incident ids are real.
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            var socket = await context.WebSockets.AcceptWebSocketAsync();

            // Registers this connection so IncidentSocketManager knows to
            // include it when broadcasting future events for this incident,
            // and tells everyone already here that someone new is watching.
            sockets.Connect(incidentId, socket, new IncidentSocketManager.Viewer(user.Id, user.Email, user.Name));
            await sockets.BroadcastPresenceAsync(incidentId);
            try
            {
                var buffer = new byte[1024];
                while (socket.State == WebSocketState.Open)
                {
                    // Clients only receive broadcasts; any inbound message is
                    // ignored, but we still need to keep receiving to detect
                    // disconnects.
                    var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (WebSocketException)
            {
                // Raised when the browser tab closes the connection abruptly.
            }
            finally
            {
                sockets.Disconnect(incidentId, socket);

                // Remove first, then announce - otherwise the person who just
                // left would still be in the list we send out.
                await sockets.BroadcastPresenceAsync(incidentId);

                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
            }
        });
    }
}
