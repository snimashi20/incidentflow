using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using IncidentFlow.Api.Common;

namespace IncidentFlow.Api.Realtime;

// Tracks WebSocket connections held by *this* server process, keyed by
// incident id. Endpoint handlers never write to these sockets directly -
// every event goes through IEventPublisher instead, and in production that
// comes back around via Redis (see RedisListenerBackgroundService), which is
// what lets this run behind multiple backend replicas without an incident's
// events getting split across processes: a message published by server A
// still reaches a browser connected to server B.
public class IncidentSocketManager
{
    // Who is on the other end of one open socket. Stored alongside the
    // connection so the server can answer "who is looking at this incident
    // right now?" without going back to the database - the user was already
    // loaded when the connection was authenticated.
    public record Viewer(int UserId, string Email, string Name);

    // Keyed by incident id, then by socket. The inner dictionary used to hold
    // a throwaway `byte` (it was only ever used as a set of sockets); it now
    // holds the Viewer, which is what makes the presence list below possible.
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<WebSocket, Viewer>> _connections = new();

    public void Connect(int incidentId, WebSocket socket, Viewer viewer)
    {
        var group = _connections.GetOrAdd(incidentId, _ => new ConcurrentDictionary<WebSocket, Viewer>());
        group[socket] = viewer;
    }

    public void Disconnect(int incidentId, WebSocket socket)
    {
        if (_connections.TryGetValue(incidentId, out var group))
        {
            group.TryRemove(socket, out _);
            if (group.IsEmpty)
            {
                _connections.TryRemove(incidentId, out _);
            }
        }
    }

    // Everyone currently watching this incident, one entry per person.
    //
    // DistinctBy(UserId) matters: one human with the incident open in three
    // browser tabs is three sockets but still one viewer, and "Dana, Dana,
    // Dana" on screen would look like a bug.
    //
    // The honest caveat: this only knows about sockets connected to THIS
    // server process. Every other event in the app survives multiple backend
    // replicas because it travels through Redis (see the class comment
    // above), but presence is held in memory here, so with two replicas each
    // browser sees only the people who happened to land on the same one.
    // The standard fix is to keep the viewer set in Redis instead - a set per
    // incident, with each connection refreshing a short TTL so a crashed
    // replica's viewers disappear on their own rather than haunting the list.
    public IReadOnlyList<Viewer> ViewersFor(int incidentId)
    {
        if (!_connections.TryGetValue(incidentId, out var group)) return [];

        return [.. group.Values
            .DistinctBy(viewer => viewer.UserId)
            .OrderBy(viewer => viewer.Name)];
    }

    // Tells everyone watching this incident who else is here. Sent on connect
    // and on disconnect, which is enough: those are the only two moments the
    // answer can change.
    public Task BroadcastPresenceAsync(int incidentId) =>
        BroadcastLocalAsync(incidentId, new
        {
            type = "presence_updated",
            data = new { viewers = ViewersFor(incidentId) },
        });

    // Sends one JSON message to every browser tab currently watching this
    // incident, on this server process only (see the class comment above).
    public async Task BroadcastLocalAsync(int incidentId, object message)
    {
        if (!_connections.TryGetValue(incidentId, out var group) || group.IsEmpty) return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonConfig.Options);
        var dead = new List<WebSocket>();

        foreach (var socket in group.Keys)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                }
                else
                {
                    dead.Add(socket);
                }
            }
            catch (Exception)
            {
                // If sending fails the socket is probably already gone; clean
                // it up after the loop instead of mutating the set mid-iteration.
                dead.Add(socket);
            }
        }

        foreach (var socket in dead)
        {
            Disconnect(incidentId, socket);
        }
    }

    // Forwards an already-serialized JSON message (as received verbatim from
    // Redis) straight to local sockets, without decoding and re-encoding it.
    public async Task BroadcastRawLocalAsync(int incidentId, string rawJson)
    {
        if (!_connections.TryGetValue(incidentId, out var group) || group.IsEmpty) return;

        var payload = Encoding.UTF8.GetBytes(rawJson);
        var dead = new List<WebSocket>();

        foreach (var socket in group.Keys)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
                }
                else
                {
                    dead.Add(socket);
                }
            }
            catch (Exception)
            {
                dead.Add(socket);
            }
        }

        foreach (var socket in dead)
        {
            Disconnect(incidentId, socket);
        }
    }
}
