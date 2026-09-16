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
    // ConcurrentDictionary has no built-in "concurrent set" partner, so we
    // use one keyed by the socket itself with a throwaway `byte` value - the
    // values are never read, only the keys matter.
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<WebSocket, byte>> _connections = new();

    public void Connect(int incidentId, WebSocket socket)
    {
        var group = _connections.GetOrAdd(incidentId, _ => new ConcurrentDictionary<WebSocket, byte>());
        group[socket] = 0;
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
