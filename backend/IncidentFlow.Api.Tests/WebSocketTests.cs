// Tests for the real-time feature itself: does a WebSocket connection
// actually receive the events our HTTP endpoints publish? WebApplicationFactory's
// TestServer can open real in-memory WebSocket connections against our app via
// Server.CreateWebSocketClient().
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class WebSocketTests : IntegrationTestBase
{
    private Uri BuildWsUri(string path, string? token)
    {
        var builder = new UriBuilder(Factory.Server.BaseAddress) { Scheme = "ws", Path = path };
        if (token is not null)
        {
            builder.Query = $"token={Uri.EscapeDataString(token)}";
        }
        return builder.Uri;
    }

    private static async Task<JsonElement> ReceiveJsonAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonSerializer.Deserialize<JsonElement>(json, Json);
    }

    [Fact]
    public async Task WebSocket_Receives_Incident_Broadcasts()
    {
        var token = await RegisterAndLoginAsync("wsuser@example.com", "WS User");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var createResponse = await Client.PostAsJsonAsync(
            "/api/incidents", new { title = "Realtime test", description = "", severity = "HIGH" }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var wsClient = Factory.Server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(BuildWsUri($"/ws/incidents/{incidentId}", token), CancellationToken.None);

        // While the socket is open, trigger a status change over plain HTTP...
        await Client.PatchAsJsonAsync($"/api/incidents/{incidentId}/status", new { status = "INVESTIGATING" }, Json);

        // ...and confirm the WebSocket receives both events the endpoint
        // publishes: the updated incident, and a new activity log entry.
        var first = await ReceiveJsonAsync(socket);
        var second = await ReceiveJsonAsync(socket);

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
            "/api/incidents", new { title = "A", description = "", severity = "LOW" }, Json
        );
        var incidentA = (await incidentAResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var incidentBResponse = await Client.PostAsJsonAsync(
            "/api/incidents", new { title = "B", description = "", severity = "LOW" }, Json
        );
        var incidentB = (await incidentBResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var wsClient = Factory.Server.CreateWebSocketClient();
        using var socketA = await wsClient.ConnectAsync(BuildWsUri($"/ws/incidents/{incidentA}", token), CancellationToken.None);

        // Update incident B first - socketA should NOT receive this one.
        await Client.PatchAsJsonAsync($"/api/incidents/{incidentB}/status", new { status = "RESOLVED" }, Json);
        // Then update incident A - this is the one socketA should receive.
        await Client.PatchAsJsonAsync($"/api/incidents/{incidentA}/status", new { status = "RESOLVED" }, Json);

        var first = await ReceiveJsonAsync(socketA);
        Assert.Equal(incidentA, first.GetProperty("data").GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task WebSocket_Rejects_Missing_Token()
    {
        // Connecting without a token should be refused, not silently accepted.
        // Our endpoint (Endpoints/WebSocketEndpoints.cs) responds 401 and never
        // completes the WebSocket handshake, so the client fails to connect at
        // all - the exact exception type TestHost throws for a failed
        // handshake isn't part of its public contract, so we assert broadly
        // that connecting throws, rather than a successfully connected socket.
        var wsClient = Factory.Server.CreateWebSocketClient();
        await Assert.ThrowsAnyAsync<Exception>(
            () => wsClient.ConnectAsync(BuildWsUri("/ws/incidents/1", token: null), CancellationToken.None)
        );
    }
}
