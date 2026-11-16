// Tests for the real-time feature itself: does a WebSocket connection
// actually receive the events our HTTP endpoints publish? WebApplicationFactory's
// TestServer can open real in-memory WebSocket connections against our app via
// Server.CreateWebSocketClient().
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class WebSocketTests : IntegrationTestBase
{
    // Builds the socket URL from an already-issued ticket. Note what is NOT
    // in this URL: the access token. The socket is opened with a 30-second
    // single-use ticket instead, precisely so that nothing long-lived ends up
    // in a log line - see Security/WebSocketTicketStore.cs.
    private Uri BuildWsUri(string path, string? ticket)
    {
        var builder = new UriBuilder(Factory.Server.BaseAddress) { Scheme = "ws", Path = path };
        if (ticket is not null)
        {
            builder.Query = $"ticket={Uri.EscapeDataString(ticket)}";
        }
        return builder.Uri;
    }

    // The two steps the frontend takes, together: swap a token for a ticket,
    // then build the URL from it.
    private async Task<Uri> WsUriForAsync(string path, string token) =>
        BuildWsUri(path, await WebSocketTicketAsync(token));

    private static async Task<JsonElement> ReceiveJsonAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonSerializer.Deserialize<JsonElement>(json, Json);
    }

    // Connecting now also announces who is watching (see the presence feature
    // in Realtime/IncidentSocketManager.cs), so a socket receives a
    // "presence_updated" message the moment it joins - and again whenever
    // anyone else comes or goes. The tests below are about incident events,
    // so this skips past presence chatter to the next real event rather than
    // asserting on whichever message happened to arrive first.
    private static async Task<JsonElement> ReceiveEventSkippingPresenceAsync(WebSocket socket)
    {
        while (true)
        {
            var message = await ReceiveJsonAsync(socket);
            if (message.GetProperty("type").GetString() != "presence_updated")
            {
                return message;
            }
        }
    }

    [Fact]
    public async Task WebSocket_Receives_Incident_Broadcasts()
    {
        var token = await RegisterAndLoginAsync("wsuser@example.com", "WS User");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await Client.PostAsJsonAsync(
            "/api/incidents", new { title = "Realtime test", description = "", severity = "HIGH", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var wsClient = Factory.Server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(await WsUriForAsync($"/ws/incidents/{incidentId}", token), CancellationToken.None);

        // While the socket is open, trigger a status change over plain HTTP...
        await Client.PatchAsJsonAsync($"/api/incidents/{incidentId}/status", new { status = "INVESTIGATING" }, Json);

        // ...and confirm the WebSocket receives both events the endpoint
        // publishes: the updated incident, and a new activity log entry.
        var first = await ReceiveEventSkippingPresenceAsync(socket);
        var second = await ReceiveEventSkippingPresenceAsync(socket);

        var types = new HashSet<string> { first.GetProperty("type").GetString()!, second.GetProperty("type").GetString()! };
        Assert.Equal(new HashSet<string> { "incident_updated", "activity_created" }, types);

        var updated = first.GetProperty("type").GetString() == "incident_updated" ? first : second;
        Assert.Equal("INVESTIGATING", updated.GetProperty("data").GetProperty("status").GetString());
    }

    [Fact]
    public async Task WebSocket_Broadcasts_Only_To_Matching_Incident()
    {
        // Makes sure incidents don't "leak" events to each other - a client
        // watching incident A should never see an update meant for incident B.
        var token = await RegisterAndLoginAsync("wsscope@example.com", "WS Scope");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var incidentAResponse = await Client.PostAsJsonAsync(
            "/api/incidents", new { title = "A", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentA = (await incidentAResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var incidentBResponse = await Client.PostAsJsonAsync(
            "/api/incidents", new { title = "B", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentB = (await incidentBResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var wsClient = Factory.Server.CreateWebSocketClient();
        using var socketA = await wsClient.ConnectAsync(await WsUriForAsync($"/ws/incidents/{incidentA}", token), CancellationToken.None);

        // Update incident B first - socketA should NOT receive this one.
        await Client.PatchAsJsonAsync($"/api/incidents/{incidentB}/status", new { status = "RESOLVED" }, Json);
        // Then update incident A - this is the one socketA should receive.
        await Client.PatchAsJsonAsync($"/api/incidents/{incidentA}/status", new { status = "RESOLVED" }, Json);

        var first = await ReceiveEventSkippingPresenceAsync(socketA);
        Assert.Equal(incidentA, first.GetProperty("data").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task WebSocket_Announces_Who_Is_Watching()
    {
        var token = await RegisterAndLoginAsync("presence@example.com", "Presence Watcher");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await Client.PostAsJsonAsync(
            "/api/incidents",
            new { title = "Who is here", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() },
            Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var wsClient = Factory.Server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(await WsUriForAsync($"/ws/incidents/{incidentId}", token), CancellationToken.None);

        // The very first thing a new connection hears is who else is here -
        // including itself, so the page can render the list immediately
        // instead of showing nobody until the next person arrives.
        var message = await ReceiveJsonAsync(socket);

        Assert.Equal("presence_updated", message.GetProperty("type").GetString());
        var viewers = message.GetProperty("data").GetProperty("viewers");
        Assert.Equal(1, viewers.GetArrayLength());
        Assert.Equal("presence@example.com", viewers[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task WebSocket_Refuses_An_Incident_You_Cannot_See()
    {
        // The REST endpoints already refuse to show an incident you aren't
        // involved in. The socket has to refuse it too - otherwise the live
        // feed would hand over everything the detail page would not, and the
        // presence list would leak the watcher's name to the real responders.
        using var ownerClient = await AuthenticatedClientAsync("ws.owner@example.com", "WS Owner");
        var strangerToken = await RegisterAndLoginAsync("ws.stranger@example.com", "WS Stranger");

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/incidents",
            new { title = "Private", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() },
            Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        // The ticket is perfectly valid - it is who it belongs to that is the
        // problem, which is the whole point of this test.
        var uri = await WsUriForAsync($"/ws/incidents/{incidentId}", strangerToken);

        var wsClient = Factory.Server.CreateWebSocketClient();
        await Assert.ThrowsAnyAsync<Exception>(() => wsClient.ConnectAsync(uri, CancellationToken.None));
    }

    [Fact]
    public async Task WebSocket_Rejects_Missing_Ticket()
    {
        // Connecting without a ticket should be refused, not silently accepted.
        // Our endpoint (Endpoints/WebSocketEndpoints.cs) responds 401 and never
        // completes the WebSocket handshake, so the client fails to connect at
        // all - the exact exception type TestHost throws for a failed
        // handshake isn't part of its public contract, so we assert broadly
        // that connecting throws, rather than a successfully connected socket.
        var wsClient = Factory.Server.CreateWebSocketClient();
        await Assert.ThrowsAnyAsync<Exception>(
            () => wsClient.ConnectAsync(BuildWsUri("/ws/incidents/1", ticket: null), CancellationToken.None)
        );
    }

    [Fact]
    public async Task WebSocket_Rejects_The_Access_Token_In_The_Url()
    {
        // The old way in, kept as a test so it cannot quietly come back.
        // ?token=<jwt> used to be how sockets authenticated; it must now be
        // worthless, because the reason for the change was that URLs get
        // written to logs.
        var token = await RegisterAndLoginAsync("ws.oldway@example.com", "WS Old Way");

        var builder = new UriBuilder(Factory.Server.BaseAddress)
        {
            Scheme = "ws",
            Path = "/ws/incidents/1",
            Query = $"token={Uri.EscapeDataString(token)}",
        };

        var wsClient = Factory.Server.CreateWebSocketClient();
        await Assert.ThrowsAnyAsync<Exception>(() => wsClient.ConnectAsync(builder.Uri, CancellationToken.None));
    }

    [Fact]
    public async Task WebSocket_Ticket_Works_Exactly_Once()
    {
        var token = await RegisterAndLoginAsync("ws.replay@example.com", "WS Replay");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await Client.PostAsJsonAsync(
            "/api/incidents",
            new { title = "Replay me", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() },
            Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var ticket = await WebSocketTicketAsync(token);
        var uri = BuildWsUri($"/ws/incidents/{incidentId}", ticket);
        var wsClient = Factory.Server.CreateWebSocketClient();

        // First use works...
        using var socket = await wsClient.ConnectAsync(uri, CancellationToken.None);

        // ...and the same URL is dead afterwards. This is what makes a ticket
        // that leaks into a log file harmless: by the time anyone reads it,
        // it has already been spent.
        await Assert.ThrowsAnyAsync<Exception>(() => wsClient.ConnectAsync(uri, CancellationToken.None));
    }

    [Fact]
    public async Task WebSocket_Receives_Deletion_After_The_Row_Is_Gone()
    {
        // The delete endpoint used to publish "incident_deleted" BEFORE
        // deleting, so a failed delete left every open page showing an
        // incident as gone when it wasn't. The publish now happens after the
        // commit - this test is the guard that moving it did not simply drop
        // the notification on the floor.
        var token = await RegisterAndLoginAsync("ws.deleter@example.com", "WS Deleter");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await Client.PostAsJsonAsync(
            "/api/incidents",
            new { title = "Doomed", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() },
            Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var wsClient = Factory.Server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(
            await WsUriForAsync($"/ws/incidents/{incidentId}", token), CancellationToken.None
        );

        var deleteResponse = await Client.DeleteAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var message = await ReceiveEventSkippingPresenceAsync(socket);
        Assert.Equal("incident_deleted", message.GetProperty("type").GetString());
        Assert.Equal(incidentId, message.GetProperty("data").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task WebSocket_Ticket_Requires_Authentication()
    {
        // No token, no ticket - otherwise the ticket endpoint would just be a
        // way to mint credentials from nothing.
        var response = await Client.PostAsync("/api/auth/ws-ticket", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
