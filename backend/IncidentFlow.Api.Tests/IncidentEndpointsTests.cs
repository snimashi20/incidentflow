// Tests for creating/listing/viewing incidents, changing status, and joining.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class IncidentEndpointsTests : IntegrationTestBase
{
    [Fact]
    public async Task CreateIncident()
    {
        using var client = await AuthenticatedClientAsync("owner@example.com", "Owner");

        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new { title = "Payment API Failure", description = "500s on checkout", severity = "HIGH" },
            Json
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Payment API Failure", body.GetProperty("title").GetString());
        Assert.Equal("OPEN", body.GetProperty("status").GetString()); // every new incident starts OPEN

        // The creator should be automatically added as a participant...
        var participants = body.GetProperty("participants");
        Assert.Equal(1, participants.GetArrayLength());
        Assert.Equal("owner@example.com", participants[0].GetProperty("email").GetString());

        // ...and get an activity log entry recorded for creating it.
        var logs = body.GetProperty("activity_logs").EnumerateArray();
        Assert.Contains(logs, log => log.GetProperty("message").GetString()!.Contains("created the incident"));
    }

    [Fact]
    public async Task CreateIncident_RequiresAuth()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/incidents", new { title = "X", description = "", severity = "LOW" }, Json
        );
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListIncidents()
    {
        using var client = await AuthenticatedClientAsync("lister@example.com", "Lister");

        await client.PostAsJsonAsync("/api/incidents", new { title = "Incident A", description = "", severity = "LOW" }, Json);
        await client.PostAsJsonAsync("/api/incidents", new { title = "Incident B", description = "", severity = "MEDIUM" }, Json);

        var response = await client.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var titles = body.EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToHashSet();
        Assert.Contains("Incident A", titles);
        Assert.Contains("Incident B", titles);
    }

    [Fact]
    public async Task UpdateStatus_LogsActivity()
    {
        using var client = await AuthenticatedClientAsync("responder@example.com", "Responder");

        var createResponse = await client.PostAsJsonAsync(
            "/api/incidents", new { title = "DB outage", description = "", severity = "CRITICAL" }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var response = await client.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/status", new { status = "INVESTIGATING" }, Json
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("INVESTIGATING", body.GetProperty("status").GetString());

        // Confirms the human-readable "before -> after" message got recorded.
        var logs = body.GetProperty("activity_logs").EnumerateArray();
        Assert.Contains(logs, log => log.GetProperty("message").GetString()!.Contains("OPEN → INVESTIGATING"));
    }

    [Fact]
    public async Task JoinIncident_AddsParticipantOnce()
    {
        using var ownerClient = await AuthenticatedClientAsync("creator@example.com", "Creator");
        using var joinerClient = await AuthenticatedClientAsync("joiner@example.com", "Joiner");

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/incidents", new { title = "Cache down", description = "", severity = "MEDIUM" }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        // Join the same incident twice as the same user...
        var response1 = await joinerClient.PostAsync($"/api/incidents/{incidentId}/join", null);
        var response2 = await joinerClient.PostAsync($"/api/incidents/{incidentId}/join", null);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // ...and confirm they only show up once in the participants list, not twice.
        var body = await response2.Content.ReadFromJsonAsync<JsonElement>(Json);
        var joinerCount = body.GetProperty("participants").EnumerateArray()
            .Count(p => p.GetProperty("email").GetString() == "joiner@example.com");
        Assert.Equal(1, joinerCount);
    }

    [Fact]
    public async Task GetMissingIncident_404()
    {
        using var client = await AuthenticatedClientAsync("ghost@example.com", "Ghost");
        var response = await client.GetAsync("/api/incidents/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
