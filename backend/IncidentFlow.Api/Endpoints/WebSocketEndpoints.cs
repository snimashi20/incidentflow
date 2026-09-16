// The actual WebSocket endpoint the frontend connects to
// (ws://.../ws/incidents/{incidentId}). Unlike a normal HTTP route, a
// WebSocket connection stays open for as long as the client is connected -
// this handler doesn't return until the browser disconnects.
using System.Net.WebSockets;
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Realtime;
using IncidentFlow.Api.Security;

namespace IncidentFlow.Api.Endpoints;

public static class WebSocketEndpoints
{
    public static void MapIncidentWebSocket(this IEndpointRouteBuilder app)
    {
        app.MapGet("/ws/incidents/{incidentId:int}", async (
            HttpContext context, int incidentId, AppDbContext db, JwtService jwt, IncidentSocketManager sockets
        ) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // Browsers can't attach custom headers (like our usual
            // "Authorization: Bearer <token>") to a WebSocket handshake, so
            // the frontend instead passes the JWT as a query string
            // parameter, e.g. ?token=xyz.
            var user = await CurrentUser.FromWebSocketQueryAsync(context, db, jwt);
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

            var socket = await context.WebSockets.AcceptWebSocketAsync();

            // Registers this connection so IncidentSocketManager knows to
            // include it when broadcasting future events for this incident.
            sockets.Connect(incidentId, socket);
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
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
            }
        });
    }
}
